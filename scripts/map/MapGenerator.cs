using System.Collections.Generic;
using Godot;

namespace NWO.Map;

public static class MapGenerator
{
    // ── Generation pipeline (Phase 9.1, retuned 9.x) ────────────────────────────
    // The world is built from independent layers rather than one noise map:
    //   1. Continental shape  — low-freq FBM + radial falloff → land/ocean height.
    //   2. Mountain layer      — domain-warped ridged Simplex, gated by a low-freq
    //                            uplift mask, so peaks form coherent directional
    //                            chains. Its strength ("relief") also rings the peaks
    //                            with foothills (Hills) so mountains don't drop
    //                            straight to flatland.
    //   3. Climate axes        — independent moisture (longitudinal) and temperature
    //                            (latitudinal + jitter) passes. Climate, not height,
    //                            drives the biome, so the map varies even where it's
    //                            flat. See docs/MAP_GENERATION.md.

    // Continental shape
    private const float BaseFrequency   = 0.045f;
    private const float DetailFrequency = 0.11f;
    private const float RadialFalloff   = 0.33f;

    // Mountain layer
    private const float WarpFrequency    = 0.05f;
    private const float WarpStrength     = 20f;    // px the ridge field is bent by
    private const float RidgeFrequency   = 0.07f;
    private const float UpliftFrequency  = 0.03f;  // where mountain belts are allowed
    private const float MountainBoost    = 0.55f;  // max height added at a ridge crest
    private const float UpliftLow        = 0.45f;  // uplift mask ramp (more belts when wider)
    private const float UpliftHigh       = 0.80f;
    private const float HillRelief       = 0.52f;  // relief above this → foothills (Hills)

    // Climate
    private const float MoistureFrequency    = 0.075f; // higher → more biome transitions
    private const float TemperatureFrequency = 0.045f;

    // Biome height bands (after the radial falloff, in [0,1] height space).
    private const float OceanLevel    = 0.25f;
    private const float CoastLevel    = 0.30f;
    private const float MountainLevel = 0.72f; // land ↔ mountain (hills come from relief)

    // Generates a map of (width x height) tiles.
    public static MapData Generate(int width, int height, int seed = 0)
    {
        var data = new MapData(width, height);

        var baseNoise   = MakeNoise(seed,     BaseFrequency);
        var detailNoise = MakeNoise(seed + 1, DetailFrequency);
        var warpNoise   = MakeNoise(seed + 2, WarpFrequency);
        var ridgeNoise  = MakeNoise(seed + 3, RidgeFrequency);
        var upliftNoise = MakeNoise(seed + 4, UpliftFrequency);
        var moistNoise  = MakeNoise(seed + 5, MoistureFrequency);
        var tempNoise   = MakeNoise(seed + 6, TemperatureFrequency);

        // Final heights kept so rivers can trace downhill after classification.
        var heights = new Dictionary<Vector2I, float>(width * height);

        for (int col = 0; col < width; col++)
        {
            for (int row = 0; row < height; row++)
            {
                var axial = EvenQOffsetToAxial(col, row);

                // 1. Continental shape: 70% base, 30% detail, remapped to [0,1].
                float h = baseNoise.GetNoise2D(col, row) * 0.7f
                        + detailNoise.GetNoise2D(col, row) * 0.3f;
                h = (h + 1f) / 2f;

                // Radial falloff: tiles far from centre become ocean (→ 2+ landmasses).
                float nx = col / (float)(width  - 1) - 0.5f;
                float ny = row / (float)(height - 1) - 0.5f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny) * 2f; // 0 at centre, ~1.4 at corners
                h -= dist * RadialFalloff;

                // 2. Mountain layer: ridged Simplex sampled through a domain warp,
                //    raised only where the uplift mask is high → coherent chains.
                //    `relief` (unsquared) is broader than the height bump (squared),
                //    so it spreads foothills around each crest.
                float wx = col + warpNoise.GetNoise2D(col,        row)        * WarpStrength;
                float wy = row + warpNoise.GetNoise2D(col + 100f, row + 100f) * WarpStrength;
                float ridge  = 1f - Mathf.Abs(ridgeNoise.GetNoise2D(wx, wy)); // crest = 1
                float uplift = (upliftNoise.GetNoise2D(col, row) + 1f) / 2f;
                float mask   = Mathf.SmoothStep(UpliftLow, UpliftHigh, uplift);
                float relief = ridge * mask;
                h += ridge * ridge * mask * MountainBoost;

                // 3. Climate: moisture (longitudinal noise) + temperature (warm at the
                //    equator, cold at the poles, with a noise wobble so it's not banded).
                float moisture = (moistNoise.GetNoise2D(col, row) + 1f) / 2f;
                float half     = (height - 1) / 2f;
                float lat      = half <= 0f ? 0f : Mathf.Abs(row - half) / half; // 0 eq → 1 pole
                float tJitter  = (tempNoise.GetNoise2D(col, row) + 1f) / 2f;
                float temperature = Mathf.Clamp((1f - lat) * 0.70f + tJitter * 0.36f, 0f, 1f);

                heights[axial]    = h;
                data.Tiles[axial] = Classify(h, moisture, temperature, relief, lat);
            }
        }

        TraceRivers(data, heights, seed);
        ScatterResources(data, seed);
        return data;
    }

    private static FastNoiseLite MakeNoise(int seed, float frequency) => new()
    {
        Seed      = seed,
        NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        Frequency = frequency,
    };

    // Traces 3–5 rivers downhill from highland (Mountain/Hills) sources. A river runs
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
            if (terrain == TerrainType.Mountain || terrain == TerrainType.Hills)
                sources.Add(axial);
        if (sources.Count == 0) return;

        // Deterministic Fisher–Yates shuffle so source choice doesn't depend on order.
        for (int i = sources.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (sources[i], sources[j]) = (sources[j], sources[i]);
        }

        int target = 3 + rng.Next(3); // 3..5
        int made   = 0;
        foreach (var src in sources)
        {
            if (made >= target) break;
            if (TraceOneRiver(data, heights, src)) made++;
        }
    }

    // Walks a path of hex corners strictly downhill from the source tile's highest
    // corner until a corner touches water (sea/lake), bottoms out in a basin, or hits
    // the step cap. Each step traverses one shared edge; commits the edges only if the
    // river ran long enough to read as one.
    private static bool TraceOneRiver(MapData data, Dictionary<Vector2I, float> heights, Vector2I start)
    {
        const int maxSteps = 80;

        var current = HighestCorner(heights, start);
        var visited = new HashSet<Vertex> { current };
        var edges   = new List<(Vector2I Tile, int Dir)>();

        for (int step = 0; step < maxSteps; step++)
        {
            if (TouchesWater(data, current)) break; // reached the sea or a lake

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
        foreach (var e in edges) data.Rivers.Add(e);
        return true;
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
            if (data.Tiles.TryGetValue(t, out var tt)
                && (tt == TerrainType.Coast || tt == TerrainType.Ocean))
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
            foreach (var (resource, chance) in CandidatesFor(terrain))
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

    // Luxuries are placed by count, not per-tile probability: 1–3 of each type land
    // on random unused tiles of an affinity terrain, so a map carries only a handful
    // of each (very sparse). Tech-revealed; +1 Gold when worked (see ResourceYields).
    private static void ScatterLuxuries(MapData data, int seed)
    {
        var rng = new System.Random(seed + 7919);
        foreach (var (luxury, terrains, maxCount) in LuxuryPlacements())
        {
            var candidates = new List<Vector2I>();
            foreach (var (axial, terrain) in data.Tiles)
                if (!data.Resources.ContainsKey(axial) && System.Array.IndexOf(terrains, terrain) >= 0)
                    candidates.Add(axial);

            int target = 1 + rng.Next(maxCount); // 1..maxCount
            for (int placed = 0; placed < target && candidates.Count > 0; placed++)
            {
                int i = rng.Next(candidates.Count);
                data.Resources[candidates[i]] = luxury;
                candidates.RemoveAt(i);
            }
        }
    }

    // (luxury, affinity terrains, max copies). Only workable land terrains — mountain
    // tiles can't be worked, so a luxury there would never yield. Mirrors the affinity
    // table in docs/MAP_GENERATION.md.
    private static IEnumerable<(ResourceType Luxury, TerrainType[] Terrains, int MaxCount)> LuxuryPlacements() => new[]
    {
        (ResourceType.Gems,    new[] { TerrainType.Hills }, 3),
        (ResourceType.GoldOre, new[] { TerrainType.Hills }, 3),
        (ResourceType.Silver,  new[] { TerrainType.Hills }, 3),
        (ResourceType.Silk,    new[] { TerrainType.Forest }, 3),
        (ResourceType.Spices,  new[] { TerrainType.Jungle, TerrainType.Forest }, 3),
        (ResourceType.Dyes,    new[] { TerrainType.Forest, TerrainType.Jungle }, 3),
        (ResourceType.Cotton,  new[] { TerrainType.Plains, TerrainType.Grassland }, 3),
        (ResourceType.Incense, new[] { TerrainType.Desert, TerrainType.Plains }, 3),
        (ResourceType.Ivory,   new[] { TerrainType.Plains, TerrainType.Grassland }, 3),
    };

    // Per-terrain resource candidates (resource, per-tile chance), rolled in order.
    // Strategic resources lead; bonus resources follow. Densities mirror
    // docs/MAP_GENERATION.md "Resource Placement Patterns".
    private static IEnumerable<(ResourceType Resource, double Chance)> CandidatesFor(TerrainType t) => t switch
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
        TerrainType.Hills => new[]
        {
            (ResourceType.Iron, 0.14), (ResourceType.Sheep, 0.08), (ResourceType.Stone, 0.07),
        },
        TerrainType.Savanna  => new[] { (ResourceType.Cattle, 0.06), (ResourceType.Sheep, 0.05) },
        TerrainType.Forest   => new[] { (ResourceType.Deer, 0.10) },
        TerrainType.Tundra   => new[] { (ResourceType.Deer, 0.08) },
        TerrainType.Jungle   => new[] { (ResourceType.Banana, 0.12) },
        TerrainType.Desert   => new[] { (ResourceType.Stone, 0.05) },
        TerrainType.Coast    => new[] { (ResourceType.Fish, 0.10) },
        TerrainType.Ocean    => new[] { (ResourceType.Fish, 0.06) },
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

    // Classifies a tile from height, climate (moisture + temperature), and mountain
    // relief. Water and mountains come from height; foothills from relief; everything
    // else from a temperature × moisture climate matrix (so flat land still varies).
    // Dry uplands read as Hills, and polar latitudes cap to Snow/Tundra.
    private static TerrainType Classify(float h, float moisture, float temperature, float relief, float lat)
    {
        if (h < OceanLevel)     return TerrainType.Ocean;
        if (h < CoastLevel)     return TerrainType.Coast;
        if (h >= MountainLevel) return TerrainType.Mountain;

        // Foothills hug the mountain belts (broad relief skirt around the crests).
        if (relief > HillRelief) return TerrainType.Hills;

        // Polar caps.
        if (lat > 0.80f) return moisture < 0.45f ? TerrainType.Snow : TerrainType.Tundra;

        int tb = temperature < 0.34f ? 0 : temperature < 0.68f ? 1 : 2; // cold / temperate / hot
        int mb = moisture    < 0.38f ? 0 : moisture    < 0.60f ? 1 : 2; // dry / mid / wet

        return (tb, mb) switch
        {
            (0, 0) => TerrainType.Tundra,
            (0, 1) => TerrainType.Forest,                                   // boreal forest
            (0, 2) => TerrainType.Forest,
            (1, 0) => TerrainType.Plains,
            (1, 1) => TerrainType.Grassland,
            (1, 2) => TerrainType.Forest,
            (2, 0) => TerrainType.Desert,
            (2, 1) => TerrainType.Savanna,
            (2, 2) => moisture > 0.78f ? TerrainType.Wetlands : TerrainType.Jungle, // hot + wet
            _      => TerrainType.Plains,
        };
    }
}
