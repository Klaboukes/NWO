using Godot;
using NWO.Map;

namespace NWO.Art;

// Procedural pixel-art generator for NWO terrain top-face tiles (Phase 7 V7.2).
//
// STYLE  (chosen direction): "detailed pixel art" — crisp 128px tiles, a 6-tone
// naturalistic ramp, and recognizable hand-placed features (outlined trees, rocks,
// cacti, faceted snow-capped peaks) over a dithered ground, finished with scattered
// decorative props and a hex-edge ambient-occlusion rim so each tile reads as a
// raised, sculpted piece. SNES-strategy / Civ V vibe, seen through the oblique
// telephoto camera.
//
// WHY PROCEDURAL  At tile scale, one shared recipe keeps all ten terrains a cohesive
// family, guarantees crisp pixels on an exact grid, and re-bakes in seconds with no
// external tool. A real PNG dropped into assets/art/tiles/ still overrides any tile
// with no code change (see TerrainTextureRegistry / the add-art-asset skill).
//
// PIPELINE  (per terrain + optional vegetation Feature, Phase 14 composites)
//   1. Ramp(base)            — 6 tones, shadow→highlight, from HexProjection
//                              .TerrainColor (so art tracks gameplay tinting).
//   2. PaintGround(...)      — Bayer-4x4 ordered dither over 2-octave value noise.
//   3. Paint<Terrain>()      — the terrain's motifs + decorative props (outlined).
//   4. Paint<Veg>Overlay()   — the feature's overlay (trees/pools/floes) painted
//                              over the base, coloured from FeatureColor's ramp.
//   5. EdgeShade(...)        — darken a rim around the hex footprint (AO).
//
// DETERMINISM  Everything is seeded from (TerrainType, Feature) only — a combo
// always bakes byte-identical art, so committed PNGs are stable in git and the
// runtime placeholder matches the baked file.
//
// TWEAKING  (see the generate-terrain-art skill for the full guide)
//   • A terrain's colour → HexProjection.TerrainColor (drives the ramp).
//   • Contrast / tones   → Ramp(...).
//   • Surface grain      → NoiseCellLarge / NoiseCellSmall in PaintGround.
//   • A terrain's look   → its Paint<Terrain> method (feature/prop counts & sizes).
//   • Tile size / rim    → TileSize / EdgeBand.
//   After any change, re-bake (generate-terrain-art skill) and run run-checks.
public static class TerrainArtGenerator
{
    public const int TileSize = 128; // px — detailed pixel art (4x the old 64px)

    private const int   EdgeBand   = 22;    // px-wide ambient-occlusion rim at the hex edge
    private const float EdgeDarken  = 0.30f; // max brightness drop at the very edge

    // Ordered-dither threshold matrix (Bayer 4x4), normalised to [0,1). Comparing a
    // per-pixel blend fraction against this gives stable, grid-aligned dithering.
    private static readonly float[,] Bayer4 =
    {
        {  0f / 16f,  8f / 16f,  2f / 16f, 10f / 16f },
        { 12f / 16f,  4f / 16f, 14f / 16f,  6f / 16f },
        {  3f / 16f, 11f / 16f,  1f / 16f,  9f / 16f },
        { 15f / 16f,  7f / 16f, 13f / 16f,  5f / 16f },
    };

    // Build the finished tile for a (terrain, vegetation-feature) combination —
    // the base terrain's ground + motifs, then the feature's overlay painted on top
    // (Grassland + Forest = trees over the meadow). Pure: same input → same Image.
    public static Image Generate(TerrainType terrain, Feature veg = Feature.None)
    {
        veg &= FeatureRules.VegMask; // Hills is geometry (a taller prism), not art

        var img  = Image.CreateEmpty(TileSize, TileSize, false, Image.Format.Rgb8);
        var rng  = new Rng(0x9E3779B97F4A7C15UL * (ulong)((int)terrain + 1)
                         + 0xBF58476D1CE4E5B9UL * (ulong)(int)veg);
        var ramp = Ramp(HexProjection.TerrainColor(terrain));
        int seed = ((int)terrain * 31 + (int)veg) * 911;

        PaintGround(img, ramp, seed);

        switch (terrain)
        {
            case TerrainType.Ocean:     PaintWater(img, ramp, rng, foam: false); break;
            case TerrainType.Coast:     PaintWater(img, ramp, rng, foam: true);  break;
            case TerrainType.Lake:      PaintLake(img, ramp, rng);      break;
            case TerrainType.Desert:    PaintDesert(img, ramp, rng);    break;
            case TerrainType.Plains:    PaintPlains(img, ramp, rng);    break;
            case TerrainType.Grassland: PaintGrassland(img, ramp, rng); break;
            case TerrainType.Tundra:    PaintTundra(img, ramp, rng);    break;
            case TerrainType.Snow:      PaintSnow(img, ramp, rng);      break;
            case TerrainType.Mountain:  PaintMountain(img, ramp, rng);  break;
            case TerrainType.Savanna:   PaintSavanna(img, ramp, rng);   break;
        }

        if (veg != Feature.None)
        {
            var fRamp = Ramp(HexProjection.FeatureColor(veg));
            switch (veg)
            {
                case Feature.Forest: PaintForestOverlay(img, fRamp, rng); break;
                case Feature.Jungle: PaintJungleOverlay(img, fRamp, rng); break;
                case Feature.Marsh:  PaintMarshOverlay(img, fRamp, rng);  break;
                case Feature.Oasis:  PaintOasisOverlay(img, fRamp, rng);  break;
                case Feature.Ice:    PaintIceOverlay(img, fRamp, rng);    break;
            }
        }

        EdgeShade(img);
        return img;
    }

    // ── Palette ───────────────────────────────────────────────────────────────

    // A 6-tone ramp from shadow (0) to highlight (5), centred on the terrain's base
    // colour (index 3). Shadows gain saturation, highlights lose a little — the way
    // real pigment reads — so it looks natural, not like a flat brightness slider.
    private static Color[] Ramp(Color b) => new[]
    {
        Shade(b, -0.34f, +0.08f),
        Shade(b, -0.20f, +0.04f),
        Shade(b, -0.09f, +0.01f),
        b,
        Shade(b, +0.13f, -0.03f),
        Shade(b, +0.26f, -0.08f),
    };

    // Nudge a colour in HSV: dv scales value, ds adds saturation, dh shifts hue.
    private static Color Shade(Color c, float dv, float ds = 0f, float dh = 0f)
    {
        float h = Mathf.PosMod(c.H + dh, 1f);
        float s = Mathf.Clamp(c.S + ds, 0f, 1f);
        float v = Mathf.Clamp(c.V * (1f + dv), 0f, 1f);
        return Color.FromHsv(h, s, v);
    }

    // ── Ground fill (ordered-dithered value-noise) ──────────────────────────────

    private const int NoiseCellLarge = 30; // broad blotches (px per lattice cell)
    private const int NoiseCellSmall = 13; // fine surface grain

    private static void PaintGround(Image img, Color[] ramp, int seed)
    {
        int last = ramp.Length - 1;
        for (int y = 0; y < TileSize; y++)
        for (int x = 0; x < TileSize; x++)
        {
            float n = 0.62f * ValueNoise(x, y, NoiseCellLarge, seed)
                    + 0.38f * ValueNoise(x, y, NoiseCellSmall, seed + 17);

            float level = n * last;
            int   lo    = Mathf.Clamp((int)level, 0, last);
            float frac  = level - lo;
            int   idx   = (frac > Bayer4[x & 3, y & 3] && lo < last) ? lo + 1 : lo;
            img.SetPixel(x, y, ramp[idx]);
        }
    }

    // ── Per-terrain motifs ──────────────────────────────────────────────────────

    // Defined wavelets: a dark trough with a bright crest just above. Coast adds foam
    // flecks for a shallows read.
    private static void PaintWater(Image img, Color[] ramp, Rng rng, bool foam)
    {
        int waves = foam ? 14 : 22;
        for (int i = 0; i < waves; i++)
        {
            int x = rng.Range(8, TileSize - 24);
            int y = rng.Range(10, TileSize - 10);
            int len = rng.Range(8, 22);
            for (int k = 0; k < len; k++)
            {
                int wy = y + (int)(2f * Mathf.Sin(k * 0.5f));
                Plot(img, x + k, wy,     ramp[5]); // crest
                Plot(img, x + k, wy + 1, ramp[1]); // trough shadow
            }
        }
        if (foam)
            for (int f = 0; f < 40; f++)
                Plot(img, rng.Range(6, TileSize - 6), rng.Range(6, TileSize - 6), Colors.White);
    }

    // Sine dune crests (lit ridge + cast shadow), wind-rippled, with scattered rocks
    // and the odd cactus.
    private static void PaintDesert(Image img, Color[] ramp, Rng rng)
    {
        for (int c = 0; c < 4; c++)
        {
            int   baseY = rng.Range(20, 108);
            float amp   = rng.Range(4, 11);
            float phase = rng.Float() * Mathf.Tau;
            for (int x = 8; x < TileSize - 8; x++)
            {
                int y = baseY + (int)(amp * Mathf.Sin(x * 0.09f + phase));
                Plot(img, x, y,     ramp[5]); // sunlit crest
                Plot(img, x, y + 1, ramp[4]);
                Plot(img, x, y + 2, ramp[1]); // shadowed lee
            }
        }
        ScatterRocks(img, ramp, rng, count: 3, minR: 2, maxR: 4);
        TryProp(img, rng, 18, (cx, cy) => DrawCactus(img, cx, cy, rng));
    }

    // Sparse prairie: short blades, a couple of dry bushes, the odd rock + flower.
    private static void PaintPlains(Image img, Color[] ramp, Rng rng)
    {
        Blades(img, ramp, rng, 60);
        Color bush = Shade(ramp[3], -0.10f, +0.10f);
        for (int b = 0; b < 2; b++)
            TryProp(img, rng, 12, (cx, cy) => DrawTree(img, cx, cy, rng.Range(4, 6), bush, lowBush: true));
        ScatterRocks(img, ramp, rng, count: 2, minR: 2, maxR: 3);
        ScatterFlowers(img, rng, 4);
    }

    // Lush meadow: dense blades, a few leafy bushes, flowers.
    private static void PaintGrassland(Image img, Color[] ramp, Rng rng)
    {
        Blades(img, ramp, rng, 130);
        Color bush = Shade(ramp[3], -0.06f, +0.12f);
        for (int b = 0; b < 3; b++)
            TryProp(img, rng, 14, (cx, cy) => DrawTree(img, cx, cy, rng.Range(4, 7), bush, lowBush: true));
        ScatterFlowers(img, rng, 7);
    }

    // Forest overlay: outlined, layered tree canopies with trunks over the base
    // ground (the base terrain's motifs show between the trees), plus underbrush.
    // `ramp` is the Forest feature ramp (HexProjection.FeatureColor), not the base's.
    private static void PaintForestOverlay(Image img, Color[] ramp, Rng rng)
    {
        Blades(img, ramp, rng, 40); // dark underbrush between the canopies
        Color leaf = Shade(ramp[3], +0.06f, +0.10f);
        int trees = rng.Range(6, 9);
        for (int i = 0; i < trees; i++)
            TryProp(img, rng, 16, (cx, cy) => DrawTree(img, cx, cy, rng.Range(8, 12), leaf));
        ScatterRocks(img, ramp, rng, count: 2, minR: 2, maxR: 4);
    }

    // Frozen scrubland: pale snow patches, dark twiggy scrub, rocks.
    private static void PaintTundra(Image img, Color[] ramp, Rng rng)
    {
        Color snow = Shade(ramp[5], +0.10f, -0.06f);
        for (int p = 0; p < 12; p++)
            Disc(img, rng.Range(14, 114), rng.Range(14, 114), rng.Range(3, 7), snow);
        for (int t = 0; t < 26; t++)
        {
            int x = rng.Range(10, 118), y = rng.Range(10, 118);
            Plot(img, x, y, ramp[0]);
            Plot(img, x, y - 1, ramp[1]);
        }
        ScatterRocks(img, ramp, rng, count: 3, minR: 2, maxR: 4);
    }

    // Snowfield: bluish shadow drifts, bright sparkles, a couple of rocks breaking
    // through the white.
    private static void PaintSnow(Image img, Color[] ramp, Rng rng)
    {
        Color shadow = Shade(ramp[2], -0.04f, +0.16f, -0.02f); // cool blue-grey drift
        for (int s = 0; s < 8; s++)
            Disc(img, rng.Range(14, 114), rng.Range(14, 114), rng.Range(4, 8), shadow);
        for (int s = 0; s < 46; s++)
            Plot(img, rng.Range(8, 120), rng.Range(8, 120), Colors.White);
        ScatterRocks(img, ramp, rng, count: 2, minR: 2, maxR: 3);
    }

    // A central snow-capped massif: an outlined rock peak, lit left face / dark right
    // face, white cap near the apex, plus a smaller shoulder peak and scree rocks.
    private static void PaintMountain(Image img, Color[] ramp, Rng rng)
    {
        int last = ramp.Length - 1;
        DrawPeak(img, ramp, peakX: 60, topY: 18, baseY: 104, spread: 0.62f, capRows: 22);
        DrawPeak(img, ramp, peakX: 92, topY: 44, baseY: 104, spread: 0.5f,  capRows: 12);
        ScatterRocks(img, ramp, rng, count: 3, minR: 2, maxR: 4);
        _ = last;
    }

    // Dry tropical grassland: tall sun-bleached blades, a couple of flat-topped
    // acacia bushes, scattered rocks. Sparser and yellower than Grassland.
    private static void PaintSavanna(Image img, Color[] ramp, Rng rng)
    {
        Blades(img, ramp, rng, 70);
        Color acacia = Shade(ramp[3], +0.04f, +0.14f);
        for (int b = 0; b < 2; b++)
            TryProp(img, rng, 16, (cx, cy) => DrawTree(img, cx, cy, rng.Range(5, 8), acacia, lowBush: true));
        ScatterRocks(img, ramp, rng, count: 3, minR: 2, maxR: 4);
    }

    // Jungle overlay: many tightly-packed dark canopies over a heavy underlayer.
    // Reads as a thicker, darker Forest. `ramp` is the Jungle feature ramp.
    private static void PaintJungleOverlay(Image img, Color[] ramp, Rng rng)
    {
        Blades(img, ramp, rng, 70);
        Color leaf  = Shade(ramp[3], +0.04f, +0.14f);
        int   trees = rng.Range(8, 12);
        for (int i = 0; i < trees; i++)
            TryProp(img, rng, 14, (cx, cy) => DrawTree(img, cx, cy, rng.Range(7, 11), leaf));
        ScatterRocks(img, ramp, rng, count: 2, minR: 2, maxR: 3);
    }

    // Marsh overlay: murky standing-water pools over the base ground, with reedy
    // tufts along their edges. Damp, low, and broken-up. `ramp` is the Marsh ramp.
    // The pool colour is explicit blue-grey water (NOT ramp-derived) so pools read
    // as water, not as dark canopy discs.
    private static void PaintMarshOverlay(Image img, Color[] ramp, Rng rng)
    {
        Color water = new(0.24f, 0.38f, 0.44f); // murky blue-grey pool
        Color bank  = Shade(ramp[1], -0.10f, +0.08f);
        for (int p = 0; p < 5; p++)
        {
            int px = rng.Range(20, 108), py = rng.Range(20, 108);
            int r  = rng.Range(6, 12);
            if (!InFootprint(px, py, r * 0.5f)) { p--; continue; }
            DiscOutlined(img, px, py, r, water, bank);
            Plot(img, px - r / 2, py - r / 2, Colors.White); // glint
        }
        Blades(img, ramp, rng, 90); // reeds
        ScatterRocks(img, ramp, rng, count: 2, minR: 2, maxR: 3);
    }

    // Oasis overlay: a central spring-fed pond ringed by lush growth and a couple of
    // palm-style trees — a green pocket in the dunes. `ramp` is the Oasis ramp.
    private static void PaintOasisOverlay(Image img, Color[] ramp, Rng rng)
    {
        Color water = new(0.22f, 0.45f, 0.62f);
        Color shore = Shade(ramp[3], -0.05f, +0.12f);
        int cx = TileSize / 2 + rng.Range(-8, 9);
        int cy = TileSize / 2 + rng.Range(-8, 9);
        int r  = rng.Range(12, 17);
        DiscOutlined(img, cx, cy, r + 3, shore, Shade(ramp[1], -0.15f)); // lush bank
        Disc(img, cx, cy, r, water);
        Plot(img, cx - r / 3, cy - r / 3, Colors.White); // sun glint
        Plot(img, cx - r / 3 + 1, cy - r / 3, Colors.White);

        Color palm = Shade(ramp[3], +0.10f, +0.10f);
        for (int t = 0; t < 3; t++)
        {
            float a  = rng.Float() * Mathf.Tau;
            int   tx = cx + (int)(Mathf.Cos(a) * (r + 6));
            int   ty = cy + (int)(Mathf.Sin(a) * (r + 6));
            if (InFootprint(tx, ty, 14)) DrawTree(img, tx, ty, rng.Range(5, 8), palm);
        }
        Blades(img, ramp, rng, 30); // reeds at the waterline
    }

    // Ice overlay: drifting white floes with cracked blue seams over the water —
    // pack ice closing the polar sea. `ramp` is the Ice feature ramp.
    private static void PaintIceOverlay(Image img, Color[] ramp, Rng rng)
    {
        Color floe   = Shade(ramp[4], +0.04f, -0.04f);
        Color floeHi = Colors.White;
        Color seam   = new(0.45f, 0.62f, 0.78f); // cold blue crack between floes
        int floes = rng.Range(7, 11);
        for (int i = 0; i < floes; i++)
        {
            int fx = rng.Range(14, 114), fy = rng.Range(14, 114);
            int r  = rng.Range(6, 14);
            if (!InFootprint(fx, fy, r * 0.4f)) { i--; continue; }
            DiscOutlined(img, fx, fy, r, floe, seam);
            Disc(img, fx - r / 3, fy - r / 3, Mathf.Max(1, r / 3), floeHi); // lit facet
        }
        for (int s = 0; s < 30; s++) // frost sparkle on the open leads
            Plot(img, rng.Range(8, 120), rng.Range(8, 120), floeHi);
    }

    // Lake: calm inland water — gentler, sparser wavelets than the sea and no foam,
    // with a few bright glints. Reads flatter and stiller than Coast/Ocean.
    private static void PaintLake(Image img, Color[] ramp, Rng rng)
    {
        for (int i = 0; i < 10; i++)
        {
            int x = rng.Range(12, TileSize - 28);
            int y = rng.Range(14, TileSize - 14);
            int len = rng.Range(6, 14);
            for (int k = 0; k < len; k++)
            {
                Plot(img, x + k, y,     ramp[5]); // soft crest
                Plot(img, x + k, y + 1, ramp[2]); // shallow shadow
            }
        }
        for (int g = 0; g < 8; g++)
            Plot(img, rng.Range(10, 118), rng.Range(10, 118), Colors.White);
    }

    private static void DrawPeak(Image img, Color[] ramp, int peakX, int topY, int baseY, float spread, int capRows)
    {
        int   last    = ramp.Length - 1;
        Color outline = Shade(ramp[0], -0.35f, +0.05f);
        for (int y = topY; y <= baseY; y++)
        {
            float half = (y - topY) * spread + 2f;
            int   x0   = (int)(peakX - half);
            int   x1   = (int)(peakX + half);
            for (int x = x0; x <= x1; x++)
            {
                float lit = (peakX - x) / (half + 1f);             // left face catches light
                int   idx = Mathf.Clamp(3 + Mathf.RoundToInt(lit * 2.5f), 0, last);
                Plot(img, x, y, (x == x0 || x == x1) ? outline : ramp[idx]); // outlined silhouette
            }
        }
        for (int y = topY; y < topY + capRows; y++)
        {
            float half = (y - topY) * (spread * 0.7f) + 1f;
            for (int x = (int)(peakX - half); x <= (int)(peakX + half); x++)
                Plot(img, x, y, Colors.White);
        }
    }

    // ── Reusable feature/prop drawers ───────────────────────────────────────────

    // A bushy tree (or low bush): a brown trunk, an outlined layered green canopy, and
    // a sun-side highlight. `green` is the canopy base; tones are derived from it.
    private static void DrawTree(Image img, int cx, int cy, int r, Color green, bool lowBush = false)
    {
        Color outline  = Shade(green, -0.55f, +0.10f);
        Color leafDark = Shade(green, -0.22f, +0.08f);
        Color leafLit  = Shade(green, +0.30f, -0.05f);
        Color trunk    = Color.FromHsv(0.07f, 0.55f, 0.34f);

        if (!lowBush)
            for (int t = 0; t < 4; t++) { Plot(img, cx, cy + r + t, trunk); Plot(img, cx - 1, cy + r + t, trunk); }

        DiscOutlined(img, cx, cy, r, leafDark, outline);            // base canopy + outline
        Disc(img, cx - r / 3, cy - r / 3, (int)(r * 0.7f), green);  // upper lump
        for (int h = 0; h < r; h++)                                 // highlight crescent
            Plot(img, cx - r / 2 + h / 2, cy - r / 2, leafLit);
    }

    // A grey boulder with an outline and a top highlight.
    private static void DrawRock(Image img, int cx, int cy, int r)
    {
        Color rock     = Color.FromHsv(0.07f, 0.10f, 0.52f);
        Color rockHi   = Color.FromHsv(0.07f, 0.06f, 0.70f);
        Color outline  = Color.FromHsv(0.07f, 0.18f, 0.28f);
        DiscOutlined(img, cx, cy, r, rock, outline);
        Plot(img, cx - r / 2, cy - r / 2, rockHi);
        if (r > 2) Plot(img, cx - r / 2 + 1, cy - r / 2, rockHi);
    }

    // A saguaro-style cactus: a vertical body with two arms.
    private static void DrawCactus(Image img, int cx, int cy, Rng rng)
    {
        Color body    = Color.FromHsv(0.30f, 0.55f, 0.45f);
        Color outline = Color.FromHsv(0.30f, 0.60f, 0.28f);
        int   h       = rng.Range(8, 13);
        for (int i = 0; i < h; i++) { Plot(img, cx, cy - i, body); Plot(img, cx + 1, cy - i, body); Plot(img, cx - 1, cy - i, outline); Plot(img, cx + 2, cy - i, outline); }
        int armY = cy - h / 2;
        for (int i = 0; i < 4; i++) { Plot(img, cx - 1 - i, armY, body); Plot(img, cx + 2 + i, armY - 1, body); }
        Plot(img, cx - 5, armY - 1, body); Plot(img, cx - 5, armY - 2, body);   // left arm up
        Plot(img, cx + 6, armY - 2, body); Plot(img, cx + 6, armY - 3, body);   // right arm up
    }

    private static void ScatterRocks(Image img, Color[] ramp, Rng rng, int count, int minR, int maxR)
    {
        _ = ramp;
        for (int i = 0; i < count; i++)
            TryProp(img, rng, 10, (cx, cy) => DrawRock(img, cx, cy, rng.Range(minR, maxR + 1)));
    }

    private static void ScatterFlowers(Image img, Rng rng, int count)
    {
        Color[] petals = { Colors.White, new(1f, 0.9f, 0.3f), new(0.95f, 0.4f, 0.5f), new(0.7f, 0.5f, 0.95f) };
        for (int i = 0; i < count; i++)
            TryProp(img, rng, 8, (cx, cy) =>
            {
                Color p = petals[(int)rng.Range(0, petals.Length)];
                Plot(img, cx, cy, p); Plot(img, cx - 1, cy, p); Plot(img, cx + 1, cy, p);
                Plot(img, cx, cy - 1, p); Plot(img, cx, cy + 1, p);
            });
    }

    private static void Blades(Image img, Color[] ramp, Rng rng, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int x = rng.Range(10, 118), y = rng.Range(14, 116);
            if (!InFootprint(x, y, 4)) { i--; continue; }
            Plot(img, x, y, ramp[1]);
            Plot(img, x, y - 1, rng.Chance(0.5f) ? ramp[5] : ramp[4]);
        }
    }

    // ── Drawing + math helpers ──────────────────────────────────────────────────

    private static void Plot(Image img, int x, int y, Color c)
    {
        if (x >= 0 && x < TileSize && y >= 0 && y < TileSize) img.SetPixel(x, y, c);
    }

    private static void Disc(Image img, int cx, int cy, int r, Color c)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dy * dy <= r * r) Plot(img, cx + dx, cy + dy, c);
    }

    // Filled disc with a 1px outline ring just outside it.
    private static void DiscOutlined(Image img, int cx, int cy, int r, Color fill, Color outline)
    {
        int ro = r + 1;
        for (int dy = -ro; dy <= ro; dy++)
        for (int dx = -ro; dx <= ro; dx++)
        {
            int d2 = dx * dx + dy * dy;
            if (d2 <= r * r)        Plot(img, cx + dx, cy + dy, fill);
            else if (d2 <= ro * ro) Plot(img, cx + dx, cy + dy, outline);
        }
    }

    // Roll a position spread across the WHOLE hex footprint and invoke `draw` there.
    // `clearance` is how far (px) the point must sit inside the hex edge, so larger
    // props (trees) stay off the rim while small ones (flowers) can sit near it — it
    // does NOT bias toward the centre. Gives up after a few tries so a crowded tile
    // can't loop forever.
    private static void TryProp(Image img, Rng rng, int clearance, System.Action<int, int> draw)
    {
        _ = img;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int cx = rng.Range(6, TileSize - 6);
            int cy = rng.Range(6, TileSize - 6);
            if (!InFootprint(cx, cy, clearance)) continue;
            draw(cx, cy);
            return;
        }
    }

    // Final pass: darken a rim around the hex footprint so the tile reads as a raised,
    // sculpted piece (a cheap ambient-occlusion edge). Pixels outside the hex are
    // never shown (the UV maps a square texture onto the hexagon), so they're skipped.
    private static void EdgeShade(Image img)
    {
        for (int y = 0; y < TileSize; y++)
        for (int x = 0; x < TileSize; x++)
        {
            float d = HexEdgeDistance(x, y);
            if (d < 0f || d >= EdgeBand) continue;
            float t = (1f - d / EdgeBand) * EdgeDarken;
            Color c = img.GetPixel(x, y);
            img.SetPixel(x, y, new Color(c.R * (1f - t), c.G * (1f - t), c.B * (1f - t)));
        }
    }

    // Signed distance from the hex edge for a flat-top hexagon inscribed in the tile
    // (circumradius = TileSize/2, centred): >0 inside, 0 on an edge. Three edge-normal
    // axes (horizontal top/bottom + two slanted pairs); apothem = 0.866 * circumradius.
    private static float HexEdgeDistance(int x, int y)
    {
        float c = TileSize / 2f;
        float dx = x - c, dy = y - c;
        float apothem = 0.8660254f * c;
        float m = Mathf.Max(Mathf.Abs(dy),
                  Mathf.Max(Mathf.Abs(0.8660254f * dx + 0.5f * dy),
                            Mathf.Abs(-0.8660254f * dx + 0.5f * dy)));
        return apothem - m;
    }

    private static bool InFootprint(int x, int y, float margin) => HexEdgeDistance(x, y) >= margin;

    // Smooth 2-octave-friendly value noise: bilinear lattice interp with smoothstep
    // weights. Returns [0,1]. Deterministic in (x, y, seed).
    private static float ValueNoise(int px, int py, int cell, int seed)
    {
        float gx = (float)px / cell, gy = (float)py / cell;
        int   x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy);
        float fx = Smooth(gx - x0),     fy = Smooth(gy - y0);

        float v00 = Hash01(x0,     y0,     seed), v10 = Hash01(x0 + 1, y0,     seed);
        float v01 = Hash01(x0,     y0 + 1, seed), v11 = Hash01(x0 + 1, y0 + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    // Cheap deterministic integer hash → [0,1]. Stable for a given (x, y, seed).
    private static float Hash01(int x, int y, int seed)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }

    // Tiny deterministic xorshift PRNG for motif placement.
    private sealed class Rng
    {
        private ulong _s;
        public Rng(ulong seed) => _s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        private ulong Next()
        {
            _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
            return _s;
        }

        public float Float() => (Next() >> 40) / (float)(1 << 24);
        public int   Range(int minIncl, int maxExcl) => minIncl + (int)(Next() % (ulong)(maxExcl - minIncl));
        public bool  Chance(float p) => Float() < p;
    }
}
