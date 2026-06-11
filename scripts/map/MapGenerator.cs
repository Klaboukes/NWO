using System.Collections.Generic;
using Godot;

namespace NWO.Map;

public static class MapGenerator
{
    // ── Generation pipeline (Phase 9.1, retuned 9.x; Phase 11: map scripts) ───────
    // The world is built from independent layers rather than one noise map:
    //   1. Continental shape  — low-freq FBM + radial falloff → land/ocean height.
    //      Parameters come from MapScriptParams so each map script (Continents,
    //      Pangaea, Archipelago, Highlands) can vary shape/falloff/uplift while
    //      sharing all downstream layers.
    //   2. Mountain layer      — domain-warped ridged Simplex, gated by a low-freq
    //                            uplift mask, so peaks form coherent directional
    //                            chains. Its strength ("relief") also rings the peaks
    //                            with foothills (Hills) so mountains don't drop
    //                            straight to flatland.
    //   3. Climate axes        — independent moisture (longitudinal) and temperature
    //                            (latitudinal + jitter) passes. Climate, not height,
    //                            drives the biome, so the map varies even where it's
    //                            flat. Shared across all scripts.
    //      See docs/MAP_GENERATION.md.

    // Mountain shape — not per-script (warp/ridge geometry is the same for all).
    private const float WarpFrequency   = 0.05f;
    private const float WarpStrength    = 20f;
    private const float RidgeFrequency  = 0.07f;

    // Climate — shared across all scripts per ROADMAP §11. ForestFrequency drives
    // the dedicated clump field FeaturePlacer grows woods from (mid-freq so forests
    // come in coherent pockets, not salt-and-pepper).
    private const float MoistureFrequency    = 0.095f;
    private const float TemperatureFrequency = 0.060f;
    private const float ForestFrequency      = 0.13f;

    // Generates a map of (width × height) tiles using the given map script.
    public static MapData Generate(int width, int height, int seed = 0,
        MapScript script = MapScript.Continents)
    {
        var data = new MapData(width, height);
        var p    = MapScriptParams.For(script);
        int total = width * height;

        var baseNoise   = MakeNoise(seed,     p.BaseFrequency);
        var detailNoise = MakeNoise(seed + 1, p.DetailFrequency);
        var warpNoise   = MakeNoise(seed + 2, WarpFrequency);
        var ridgeNoise  = MakeNoise(seed + 3, RidgeFrequency);
        var upliftNoise = MakeNoise(seed + 4, p.UpliftFrequency);
        var moistNoise  = MakeNoise(seed + 5, MoistureFrequency);
        var tempNoise   = MakeNoise(seed + 6, TemperatureFrequency);
        var hillNoise   = MakeNoise(seed + 7, p.HillFrequency);
        var forestNoise = MakeNoise(seed + 8, ForestFrequency);

        // Pass 1: compute raw heights + climate data for every tile.
        var raw     = new Dictionary<Vector2I, (float h, float hilly, float relief)>(total);
        var climate = new Dictionary<Vector2I, ClimateSample>(total);

        for (int col = 0; col < width; col++)
        for (int row = 0; row < height; row++)
        {
            var axial = EvenQOffsetToAxial(col, row);

            // 1. Continental shape: 70% base, 30% detail, remapped to [0,1].
            float h = baseNoise.GetNoise2D(col, row) * 0.7f
                    + detailNoise.GetNoise2D(col, row) * 0.3f;
            h = (h + 1f) / 2f;

            // Radial falloff: tiles far from centre become ocean (→ 2+ landmasses).
            float nx   = col / (float)(width  - 1) - 0.5f;
            float ny   = row / (float)(height - 1) - 0.5f;
            float dist = Mathf.Sqrt(nx * nx + ny * ny) * 2f; // 0 at centre, ~1.4 at corners
            h -= dist * p.RadialFalloff;

            // 2. Mountain layer: ridged Simplex sampled through a domain warp,
            //    raised only where the uplift mask is high → coherent chains.
            //    `relief` (unsquared) is broader than the height bump (squared),
            //    so it spreads foothills around each crest.
            float wx     = col + warpNoise.GetNoise2D(col,        row)        * WarpStrength;
            float wy     = row + warpNoise.GetNoise2D(col + 100f, row + 100f) * WarpStrength;
            float ridge  = 1f - Mathf.Abs(ridgeNoise.GetNoise2D(wx, wy)); // crest = 1
            float uplift = (upliftNoise.GetNoise2D(col, row) + 1f) / 2f;
            float mask   = Mathf.SmoothStep(p.UpliftLow, p.UpliftHigh, uplift);
            float relief = ridge * mask;
            h += ridge * ridge * mask * p.MountainBoost;

            // 3. Climate: moisture (longitudinal) + temperature (latitudinal + jitter).
            float moisture    = (moistNoise.GetNoise2D(col, row) + 1f) / 2f;
            float half        = (height - 1) / 2f;
            float lat         = half <= 0f ? 0f : Mathf.Abs(row - half) / half; // 0 eq → 1 pole
            float tJitter     = (tempNoise.GetNoise2D(col, row) + 1f) / 2f;
            float temperature = Mathf.Clamp((1f - lat) * 0.70f + tJitter * 0.36f, 0f, 1f);
            float hilly       = (hillNoise.GetNoise2D(col, row) + 1f) / 2f;
            float forest      = (forestNoise.GetNoise2D(col, row) + 1f) / 2f;

            // The temperature jitter doubles as the band-edge raggedness: effective
            // latitude wobbles ±0.10 so the snow/tundra borders aren't straight rows.
            float effLat = lat + (tJitter - 0.5f) * 0.20f;

            raw[axial]     = (h, hilly, relief);
            climate[axial] = new ClimateSample(moisture, temperature, effLat, forest);
        }

        // Percentile OceanLevel (Civ 5 trick): sort all heights and find the value
        // at the (1 − TargetLandPercent) percentile so the land-ratio is stable
        // across seeds. Use whichever is higher — the authored floor or the percentile
        // value — so a script can't accidentally go below its minimum ocean level.
        var sortedH  = new float[total];
        int si       = 0;
        foreach (var v in raw.Values) sortedH[si++] = v.h;
        System.Array.Sort(sortedH);
        int   cutoffIdx    = Mathf.Clamp((int)((1f - p.TargetLandPercent) * total), 0, total - 1);
        float effOcean     = Mathf.Max(p.OceanLevel, sortedH[cutoffIdx]);

        // Pass 2: classify base terrain (latitude bands) and apply Hills using the
        // calibrated thresholds. Vegetation comes later (FeaturePlacer) so it can
        // read the FINAL terrain, after lakes/coasts/rivers.
        var heights = new Dictionary<Vector2I, float>(total);
        foreach (var (axial, (h, hilly, relief)) in raw)
        {
            heights[axial]    = h;
            var terrain       = Classify(h, climate[axial], effOcean, p.MountainLevel);
            data.Tiles[axial] = terrain;

            // Hills is a feature on top of the base biome: the mountain-relief skirt
            // (foothills) plus an independent mid-freq hilliness field, on any open
            // land terrain.
            if ((relief > p.HillRelief || hilly > p.HillThreshold)
                && FeatureRules.IsLegal(terrain, Feature.Hills))
                data.Features[axial] = Feature.Hills;
        }

        // Post-classification geography (Phase 14): water is labelled by adjacency,
        // not height — enclosed basins become lakes, then land gets ringed by Coast
        // with a probabilistic shelf. Rivers run after so termini see final water;
        // features run after rivers so an Oasis can veto river-adjacent desert.
        MapPostProcess.FormLakes(data);
        MapPostProcess.FormCoasts(data, p.ShelfChance, seed);

        TraceRivers(data, heights, seed);
        FeaturePlacer.Place(data, climate, p, seed);
        ScatterResources(data, seed);
        return data;
    }

    private static FastNoiseLite MakeNoise(int seed, float frequency) => new()
    {
        Seed      = seed,
        NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        Frequency = frequency,
    };

    // Traces rivers downhill from highland (Mountain/Hills) sources — the count
    // scales with available sources (see TraceRivers' clamp). A river runs
    // ALONG hex edges (not across them): we walk a path of hex corners (vertices)
    // strictly downhill, and each step traverses one shared edge. Consecutive edges
    // meet at a vertex, so the recorded edge-set renders as a continuous channel.
    // Deterministic per seed.
    private const int RiverMinLength = 3;

    // A vertex is the meeting point of (up to) three tiles — a hex corner. We key it
    // by the sorted triple of those tiles so the same corner compares equal no matter
    // which tile/corner it was reached from.
    private readonly struct Vertex : System.IEquatable<Vertex>
    {
        public readonly Vector2I A, B, C;

        public Vertex(Vector2I p, Vector2I q, Vector2I r)
        {
            // Sort the three tiles (X then Y) into a canonical order.
            var arr = new[] { p, q, r };
            System.Array.Sort(arr, (u, v) => u.X != v.X ? u.X - v.X : u.Y - v.Y);
            A = arr[0]; B = arr[1]; C = arr[2];
        }

        public bool Equals(Vertex o) => A == o.A && B == o.B && C == o.C;
        public override bool Equals(object? o) => o is Vertex v && Equals(v);
        public override int GetHashCode() => System.HashCode.Combine(A, B, C);
    }

    private static void TraceRivers(MapData data, Dictionary<Vector2I, float> heights, int seed)
    {
        var rng = new System.Random(seed + 4242);

        var sources = new List<Vector2I>();
        foreach (var (axial, terrain) in data.Tiles)
            if (terrain == TerrainType.Mountain || data.IsHill(axial))
                sources.Add(axial);
        if (sources.Count == 0) return;

        // Deterministic Fisher–Yates shuffle so source choice doesn't depend on order.
        for (int i = sources.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (sources[i], sources[j]) = (sources[j], sources[i]);
        }

        // Scale river count with available highland sources (capped to a sane range),
        // so larger/hillier landmasses get more rivers.
        int target = Mathf.Clamp(sources.Count / 12, 8, 18);
        int made   = 0;
        foreach (var src in sources)
        {
            if (made >= target) break;
            if (TraceOneRiver(data, heights, src)) made++;
        }
    }

    // Walks a path of hex corners strictly downhill from the source tile's highest
    // corner until a corner touches water (sea/lake), bottoms out in a basin, or hits
    // the step cap. Each step traverses one shared edge. A river always ends in water:
    // if it stops inland (basin or step cap), we carve a lake at its terminus. Commits
    // only if the river ran long enough to read as one.
    private static bool TraceOneRiver(MapData data, Dictionary<Vector2I, float> heights, Vector2I start)
    {
        const int maxSteps = 120;

        var current = HighestCorner(heights, start);
        var visited = new HashSet<Vertex> { current };
        var edges   = new List<(Vector2I Tile, int Dir)>();
        bool reachedWater = false;

        for (int step = 0; step < maxSteps; step++)
        {
            if (TouchesWater(data, current)) { reachedWater = true; break; } // sea or lake

            float    bestH = VertexHeight(heights, current);
            Vertex   bestV = default;
            (Vector2I p, Vector2I q) bestEdge = default;
            bool     found = false;

            foreach (var (nv, p, q) in VertexNeighbours(current))
            {
                if (visited.Contains(nv)) continue;
                float nh = VertexHeight(heights, nv);
                if (nh < bestH) { bestH = nh; bestV = nv; bestEdge = (p, q); found = true; }
            }
            if (!found) break; // local minimum

            edges.Add(EdgeId(bestEdge.p, bestEdge.q));
            current = bestV;
            visited.Add(current);
        }

        if (edges.Count < RiverMinLength) return false;
        if (!reachedWater) CarveLake(data, heights, current); // guarantee it ends in water
        foreach (var e in edges) data.Rivers.Add(e);
        return true;
    }

    // Turns a river's inland terminus into a lake: the lowest on-map tile of the
    // bottomed-out vertex becomes Lake, so the channel visibly empties into real
    // inland water (it also loses any land features it carried).
    private static void CarveLake(MapData data, Dictionary<Vector2I, float> heights, Vertex v)
    {
        Vector2I lowest = default;
        float    lowH   = float.MaxValue;
        bool     any    = false;
        foreach (var t in new[] { v.A, v.B, v.C })
            if (heights.TryGetValue(t, out var th) && th < lowH) { lowH = th; lowest = t; any = true; }
        if (!any) return;
        data.Tiles[lowest] = TerrainType.Lake;
        data.Features.Remove(lowest);
        data.Resources.Remove(lowest);
    }

    // The source tile's highest corner — gives the river the longest downhill run.
    private static Vertex HighestCorner(Dictionary<Vector2I, float> heights, Vector2I tile)
    {
        Vertex best = default;
        float  bestH = float.MinValue;
        for (int c = 0; c < 6; c++)
        {
            var (a, b, cc) = HexGrid.CornerTiles(tile, c);
            var v = new Vertex(a, b, cc);
            float h = VertexHeight(heights, v);
            if (h > bestH) { bestH = h; best = v; }
        }
        return best;
    }

    // The three corners adjacent to `v` along its three edges, with the tile pair that
    // edge separates. Each edge {p,q} leads to the corner shared by p, q and their
    // common neighbour on the far side of the current vertex.
    private static IEnumerable<(Vertex Vertex, Vector2I P, Vector2I Q)> VertexNeighbours(Vertex v)
    {
        var tiles = new[] { v.A, v.B, v.C };
        for (int i = 0; i < 3; i++)
        {
            var p     = tiles[i];
            var q     = tiles[(i + 1) % 3];
            var third = tiles[(i + 2) % 3];
            foreach (var d in CommonNeighbours(p, q))
            {
                if (d == third) continue;        // that's the corner we came from
                yield return (new Vertex(p, q, d), p, q);
            }
        }
    }

    // Tiles adjacent to both a and b (exactly two for an adjacent pair).
    private static IEnumerable<Vector2I> CommonNeighbours(Vector2I a, Vector2I b)
    {
        var nb = new HashSet<Vector2I>(HexGrid.GetNeighbors(b));
        foreach (var n in HexGrid.GetNeighbors(a))
            if (nb.Contains(n)) yield return n;
    }

    // Mean height of a vertex's on-map tiles (off-map corners are ignored).
    private static float VertexHeight(Dictionary<Vector2I, float> heights, Vertex v)
    {
        float sum = 0f; int n = 0;
        foreach (var t in new[] { v.A, v.B, v.C })
            if (heights.TryGetValue(t, out var h)) { sum += h; n++; }
        return n == 0 ? float.MaxValue : sum / n;
    }

    private static bool TouchesWater(MapData data, Vertex v)
    {
        foreach (var t in new[] { v.A, v.B, v.C })
            if (data.Tiles.TryGetValue(t, out var tt) && TerrainYields.IsWater(tt))
                return true;
        return false;
    }

    // Canonical (tile, dir) for the edge between adjacent tiles p and q — always from
    // the lower-ordered tile so the same edge isn't stored twice.
    private static (Vector2I Tile, int Dir) EdgeId(Vector2I p, Vector2I q)
    {
        if (q.X < p.X || (q.X == p.X && q.Y < p.Y)) (p, q) = (q, p);
        for (int d = 0; d < 6; d++)
            if (p + HexGrid.Directions[d] == q) return (p, d);
        return (p, 0); // unreachable for adjacent tiles
    }

    // Sprinkles strategic + bonus resources onto eligible terrain. For each tile we
    // roll its terrain's candidate list in order and assign the first hit (one
    // resource per tile). Seeded off the map seed (offset so it doesn't correlate
    // with the height noise) so a given seed always yields the same layout.
    private static void ScatterResources(MapData data, int seed)
    {
        // System.Random (not Godot's RNG) keeps placement deterministic and free
        // of engine init. Iterates Tiles in insertion order (col/row), which is
        // stable for a no-deletion dictionary, so a seed always yields the same map.
        var rng = new System.Random(seed + 1337);
        foreach (var (axial, terrain) in data.Tiles)
        {
            foreach (var (resource, chance) in CandidatesFor(terrain, data.FeatureAt(axial)))
            {
                if (rng.NextDouble() < chance)
                {
                    data.Resources[axial] = resource;
                    break; // one resource per tile
                }
            }
        }

        ScatterLuxuries(data, seed);
    }

    // Resources eligible on a tile: feature-driven resources first (Iron lives on
    // hills, Deer in forests, Banana in jungles — features, not terrains, since
    // Phase 14), then the base terrain's resources — so a Grassland + Hills tile can
    // roll Sheep/Iron or Cattle/Wheat. Marsh/Oasis/Ice tiles carry nothing.
    private static IEnumerable<(ResourceType Resource, double Chance)> CandidatesFor(TerrainType t, Feature f)
    {
        if ((f & (Feature.Marsh | Feature.Oasis | Feature.Ice)) != 0) yield break;
        if ((f & Feature.Hills) != 0)
        {
            yield return (ResourceType.Iron,  0.12);
            yield return (ResourceType.Sheep, 0.07);
            yield return (ResourceType.Stone, 0.06);
        }
        if ((f & Feature.Forest) != 0)
        {
            yield return (ResourceType.Deer, 0.10);
            yield break; // canopy hides the open-ground resources below
        }
        if ((f & Feature.Jungle) != 0)
        {
            yield return (ResourceType.Banana, 0.12);
            yield break;
        }
        foreach (var c in TerrainCandidates(t)) yield return c;
    }

    // Luxuries are placed by count, not per-tile probability: 1–3 of each type land
    // on random unused tiles of an affinity terrain, so a map carries only a handful
    // of each (very sparse). Tech-revealed; +1 Gold when worked (see ResourceYields).
    private static void ScatterLuxuries(MapData data, int seed)
    {
        var rng = new System.Random(seed + 7919);
        foreach (var (luxury, terrains, feature, maxCount) in LuxuryPlacements())
        {
            var candidates = new List<Vector2I>();
            foreach (var (axial, terrain) in data.Tiles)
            {
                if (data.Resources.ContainsKey(axial)) continue;
                bool ok = feature != Feature.None
                    ? data.HasFeature(axial, feature)
                    : System.Array.IndexOf(terrains, terrain) >= 0
                      && !data.HasFeature(axial, FeatureRules.VegMask); // open ground only
                if (ok) candidates.Add(axial);
            }

            int target = 1 + rng.Next(maxCount); // 1..maxCount
            for (int placed = 0; placed < target && candidates.Count > 0; placed++)
            {
                int i = rng.Next(candidates.Count);
                data.Resources[candidates[i]] = luxury;
                candidates.RemoveAt(i);
            }
        }
    }

    // (luxury, affinity terrains, required feature, max copies). Gems/GoldOre/Silver
    // sit on Hills-feature tiles, Silk/Spices/Dyes in Forest/Jungle canopy (features,
    // not terrains, since Phase 14); the rest on open affinity terrain. Only workable
    // land — mountains can't be worked. Mirrors docs/MAP_GENERATION.md.
    private static IEnumerable<(ResourceType Luxury, TerrainType[] Terrains, Feature RequiredFeature, int MaxCount)> LuxuryPlacements() => new[]
    {
        (ResourceType.Gems,    System.Array.Empty<TerrainType>(), Feature.Hills, 3),
        (ResourceType.GoldOre, System.Array.Empty<TerrainType>(), Feature.Hills, 3),
        (ResourceType.Silver,  System.Array.Empty<TerrainType>(), Feature.Hills, 3),
        (ResourceType.Silk,    System.Array.Empty<TerrainType>(), Feature.Forest, 3),
        (ResourceType.Spices,  System.Array.Empty<TerrainType>(), Feature.Jungle | Feature.Forest, 3),
        (ResourceType.Dyes,    System.Array.Empty<TerrainType>(), Feature.Forest | Feature.Jungle, 3),
        (ResourceType.Cotton,  new[] { TerrainType.Plains, TerrainType.Grassland }, Feature.None, 3),
        (ResourceType.Incense, new[] { TerrainType.Desert, TerrainType.Plains },    Feature.None, 3),
        (ResourceType.Ivory,   new[] { TerrainType.Plains, TerrainType.Grassland }, Feature.None, 3),
    };

    // Per-terrain resource candidates (resource, per-tile chance), rolled in order.
    // Strategic resources lead; bonus resources follow. Densities mirror
    // docs/MAP_GENERATION.md "Resource Placement Patterns".
    private static IEnumerable<(ResourceType Resource, double Chance)> TerrainCandidates(TerrainType t) => t switch
    {
        TerrainType.Grassland => new[]
        {
            (ResourceType.Horses, 0.06), (ResourceType.Cattle, 0.08),
            (ResourceType.Sheep,  0.06), (ResourceType.Wheat,  0.08),
        },
        TerrainType.Plains => new[]
        {
            (ResourceType.Horses, 0.06), (ResourceType.Wheat, 0.09), (ResourceType.Stone, 0.06),
        },
        TerrainType.Savanna  => new[] { (ResourceType.Cattle, 0.06), (ResourceType.Sheep, 0.05) },
        TerrainType.Tundra   => new[] { (ResourceType.Deer, 0.08) },
        TerrainType.Desert   => new[] { (ResourceType.Stone, 0.05) },
        // Fish stays off Ocean: cities can't work Ocean tiles (CityWorkforceService
        // .Workable), so Ocean Fish would be decorative. Coast density is raised a
        // notch to keep overall fish counts similar.
        TerrainType.Coast    => new[] { (ResourceType.Fish, 0.12) },
        _                    => System.Array.Empty<(ResourceType, double)>(),
    };

    // Even-q offset → axial for flat-top hexes.
    // Even columns are not vertically shifted; odd columns shift down by half a hex.
    private static Vector2I EvenQOffsetToAxial(int col, int row)
    {
        int q = col;
        int r = row - (col - (col & 1)) / 2;
        return new Vector2I(q, r);
    }

    // Classifies a tile's base biome from height + Civ5-style latitude bands
    // (Phase 14.3): water and mountains come from height; land walks the bands from
    // pole to equator — snow/tundra caps, then dryness decides desert vs steppe,
    // heat + dryness the savanna belt, and moisture splits the grass/plains
    // heartland. ClimateSample.Latitude carries the temperature jitter, so band
    // edges are ragged, not straight rows. Vegetation (Forest/Jungle/Marsh/Oasis/
    // Ice) is placed later by FeaturePlacer; Hills in Generate via FeatureRules.
    private static TerrainType Classify(float h, ClimateSample c,
        float oceanLevel, float mountainLevel)
    {
        // All water is provisionally Ocean; Coast and Lake are derived from
        // adjacency afterwards (MapPostProcess), not from a height band.
        if (h < oceanLevel)     return TerrainType.Ocean;
        if (h >= mountainLevel) return TerrainType.Mountain;

        if (c.Latitude > 0.86f) return TerrainType.Snow;          // polar cap
        if (c.Latitude > 0.72f) return TerrainType.Tundra;        // subpolar band

        if (c.Moisture < 0.34f)                                   // the dry belt
            return c.Temperature > 0.50f ? TerrainType.Desert     // hot → true desert
                                         : TerrainType.Plains;    // cold → dry steppe
        if (c.Temperature > 0.72f && c.Moisture < 0.55f)
            return TerrainType.Savanna;                           // hot semi-arid band
        return c.Moisture < 0.52f ? TerrainType.Plains
                                  : TerrainType.Grassland;        // wet heartland
    }
}
