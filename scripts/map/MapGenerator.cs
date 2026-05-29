using Godot;

namespace NWO.Map;

public static class MapGenerator
{
    // Generates a map of (width x height) tiles using two layers of simplex noise.
    // A radial falloff pushes edges toward ocean, producing island-like continents.
    public static MapData Generate(int width, int height, int seed = 0)
    {
        var data = new MapData(width, height);

        // Base continental shape
        var baseNoise = new FastNoiseLite();
        baseNoise.Seed = seed;
        baseNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        baseNoise.Frequency = 0.04f;

        // Detail layer to break up smooth terrain boundaries
        var detailNoise = new FastNoiseLite();
        detailNoise.Seed = seed + 1;
        detailNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        detailNoise.Frequency = 0.10f;

        for (int col = 0; col < width; col++)
        {
            for (int row = 0; row < height; row++)
            {
                var axial = EvenQOffsetToAxial(col, row);

                // Blend two noise layers: 70% base, 30% detail
                float h = baseNoise.GetNoise2D(col, row) * 0.7f
                        + detailNoise.GetNoise2D(col, row) * 0.3f;

                // Remap from [-1, 1] to [0, 1]
                h = (h + 1f) / 2f;

                // Radial falloff: tiles far from centre become ocean.
                // This naturally produces 2+ landmasses (the noise peaks above 0.5).
                float nx = col / (float)(width  - 1) - 0.5f;
                float ny = row / (float)(height - 1) - 0.5f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny) * 2f; // 0 at centre, ~1.4 at corners
                h -= dist * 0.35f;

                data.Tiles[axial] = HeightToTerrain(h);
            }
        }

        ScatterResources(data, seed);
        return data;
    }

    // Sprinkles strategic resources onto eligible terrain with a fixed per-tile
    // probability. Seeded off the map seed (offset so it doesn't correlate with
    // the height noise) so a given seed always yields the same resource layout.
    private const float HorsesChance = 0.04f; // of eligible Plains/Grassland tiles
    private const float IronChance   = 0.10f; // of Hills tiles (rarer terrain → higher rate)

    private static void ScatterResources(MapData data, int seed)
    {
        // System.Random (not Godot's RNG) keeps placement deterministic and free
        // of engine init. Iterates Tiles in insertion order (col/row), which is
        // stable for a no-deletion dictionary, so a seed always yields the same map.
        var rng = new System.Random(seed + 1337);
        foreach (var (axial, terrain) in data.Tiles)
        {
            switch (terrain)
            {
                case TerrainType.Plains:
                case TerrainType.Grassland:
                    if (rng.NextDouble() < HorsesChance) data.Resources[axial] = ResourceType.Horses;
                    break;
                case TerrainType.Hills:
                    if (rng.NextDouble() < IronChance) data.Resources[axial] = ResourceType.Iron;
                    break;
            }
        }
    }

    // Even-q offset → axial for flat-top hexes.
    // Even columns are not vertically shifted; odd columns shift down by half a hex.
    private static Vector2I EvenQOffsetToAxial(int col, int row)
    {
        int q = col;
        int r = row - (col - (col & 1)) / 2;
        return new Vector2I(q, r);
    }

    private static TerrainType HeightToTerrain(float h)
    {
        if (h < 0.25f) return TerrainType.Ocean;
        if (h < 0.30f) return TerrainType.Coast;
        if (h < 0.37f) return TerrainType.Desert;
        if (h < 0.47f) return TerrainType.Plains;
        if (h < 0.57f) return TerrainType.Grassland;
        if (h < 0.64f) return TerrainType.Forest;
        if (h < 0.72f) return TerrainType.Hills;
        if (h < 0.80f) return TerrainType.Tundra;
        if (h < 0.88f) return TerrainType.Snow;
        return TerrainType.Mountain;
    }
}
