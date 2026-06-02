using System.Collections.Generic;
using Godot;

namespace NWO.Map;

public static class MapGenerator
{
    // ── Generation pipeline (Phase 9.1) ─────────────────────────────────────────
    // The world is built from three independent layers rather than one noise map:
    //   1. Continental shape  — low-freq FBM + radial falloff → land/ocean height.
    //   2. Mountain layer      — domain-warped ridged Simplex, gated by a low-freq
    //                            uplift mask, so peaks form coherent directional
    //                            chains instead of isolated blobs.
    //   3. Moisture axis       — a separate low-freq pass, independent of height.
    // Height + moisture then map to a biome via HeightMoistureToBiome, which unlocks
    // climate-driven terrain (Savanna / Jungle / Wetlands). See docs/MAP_GENERATION.md.

    // Continental shape
    private const float BaseFrequency   = 0.04f;
    private const float DetailFrequency = 0.10f;
    private const float RadialFalloff   = 0.35f;

    // Mountain layer
    private const float WarpFrequency    = 0.05f;
    private const float WarpStrength     = 18f;   // px the ridge field is bent by
    private const float RidgeFrequency   = 0.06f;
    private const float UpliftFrequency  = 0.025f; // where mountain belts are allowed
    private const float MountainBoost    = 0.42f;  // max height added at a ridge crest

    // Moisture
    private const float MoistureFrequency = 0.03f;

    // Biome height bands (after the radial falloff, in [0,1] height space).
    private const float OceanLevel    = 0.25f;
    private const float CoastLevel    = 0.30f;
    private const float LowlandLevel  = 0.45f; // low ↔ mid
    private const float UplandLevel   = 0.60f; // mid ↔ upland
    private const float MountainLevel = 0.78f; // upland ↔ mountain

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
                float wx = col + warpNoise.GetNoise2D(col,        row)        * WarpStrength;
                float wy = row + warpNoise.GetNoise2D(col + 100f, row + 100f) * WarpStrength;
                float ridge  = 1f - Mathf.Abs(ridgeNoise.GetNoise2D(wx, wy)); // crest = 1
                float uplift = (upliftNoise.GetNoise2D(col, row) + 1f) / 2f;
                float mask   = Mathf.SmoothStep(0.55f, 0.85f, uplift);
                h += ridge * ridge * mask * MountainBoost;

                // 3. Moisture: independent low-freq pass in [0,1].
                float moisture = (moistNoise.GetNoise2D(col, row) + 1f) / 2f;

                data.Tiles[axial] = HeightMoistureToBiome(h, moisture, row, height);
            }
        }

        ScatterResources(data, seed);
        return data;
    }

    private static FastNoiseLite MakeNoise(int seed, float frequency) => new()
    {
        Seed      = seed,
        NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        Frequency = frequency,
    };

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
            (ResourceType.Horses, 0.04), (ResourceType.Cattle, 0.05),
            (ResourceType.Sheep,  0.04), (ResourceType.Wheat,  0.05),
        },
        TerrainType.Plains => new[]
        {
            (ResourceType.Horses, 0.04), (ResourceType.Wheat, 0.06), (ResourceType.Stone, 0.04),
        },
        TerrainType.Hills => new[]
        {
            (ResourceType.Iron, 0.10), (ResourceType.Sheep, 0.05), (ResourceType.Stone, 0.04),
        },
        TerrainType.Forest   => new[] { (ResourceType.Deer, 0.06) },
        TerrainType.Tundra   => new[] { (ResourceType.Deer, 0.06) },
        TerrainType.Jungle   => new[] { (ResourceType.Banana, 0.08) },
        TerrainType.Coast    => new[] { (ResourceType.Fish, 0.07) },
        TerrainType.Ocean    => new[] { (ResourceType.Fish, 0.05) },
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

    // Maps a (height, moisture) pair to a biome. Water and mountains are decided by
    // height alone; everything between is a height-band × moisture-band lookup, with
    // a polar override near the top/bottom map edges (Snow when dry, else Tundra).
    // Table mirrors docs/MAP_GENERATION.md "Adding a Moisture Axis".
    private static TerrainType HeightMoistureToBiome(float h, float moisture, int row, int mapHeight)
    {
        if (h < OceanLevel)    return TerrainType.Ocean;
        if (h < CoastLevel)    return TerrainType.Coast;
        if (h >= MountainLevel) return TerrainType.Mountain;

        // Latitude 0 at the equator → 1 at the poles.
        float half = (mapHeight - 1) / 2f;
        float lat  = half <= 0f ? 0f : Mathf.Abs(row - half) / half;
        if (lat > 0.82f) return moisture < 0.40f ? TerrainType.Snow : TerrainType.Tundra;

        int hb = h < LowlandLevel ? 0 : h < UplandLevel ? 1 : 2; // low / mid / upland
        int mb = moisture < 0.40f ? 0 : moisture < 0.66f ? 1 : 2; // dry / mid / wet

        return (hb, mb) switch
        {
            (0, 0) => TerrainType.Desert,
            (0, 1) => TerrainType.Plains,
            (0, 2) => TerrainType.Grassland,
            (1, 0) => TerrainType.Savanna,
            (1, 1) => TerrainType.Grassland,
            (1, 2) => moisture > 0.80f ? TerrainType.Wetlands : TerrainType.Jungle,
            (2, 0) => TerrainType.Hills,
            (2, 1) => TerrainType.Forest,
            (2, 2) => TerrainType.Forest,
            _      => TerrainType.Plains,
        };
    }
}
