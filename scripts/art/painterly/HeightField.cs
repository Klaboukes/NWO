using Godot;

namespace NWO.Art.Painterly;

// A float height buffer for terrain relief: stack noise into it, then read
// normals (for sun lighting) and ambient occlusion (for hollows). Heights are
// in arbitrary "relief units" — Normal's strength parameter sets how steep a
// unit of height reads.
public sealed class HeightField
{
    private readonly float[] _h;

    public HeightField(int width, int height)
    {
        Width  = width;
        Height = height;
        _h     = new float[width * height];
    }

    public int Width  { get; }
    public int Height { get; }

    public float this[int x, int y]
    {
        get => _h[y * Width + x];
        set => _h[y * Width + x] = value;
    }

    public void AddFbm(int seed, float scale, float amp, int octaves, float warp = 0f)
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            float sx = x / scale, sy = y / scale;
            if (warp > 0f)
            {
                var o = NoiseField.Warp(sx, sy, seed + 99, warp);
                sx += o.X; sy += o.Y;
            }
            _h[y * Width + x] += amp * NoiseField.Fbm(sx, sy, seed, octaves);
        }
    }

    public void AddRidged(int seed, float scale, float amp, int octaves)
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            _h[y * Width + x] += amp * NoiseField.Ridged(x / scale, y / scale, seed, octaves);
    }

    // Surface normal from central differences (image coords: y down, z out).
    public Vector3 Normal(int x, int y, float strength)
    {
        float dx = (At(x + 1, y) - At(x - 1, y)) * 0.5f;
        float dy = (At(x, y + 1) - At(x, y - 1)) * 0.5f;
        return new Vector3(-dx * strength, -dy * strength, 1f).Normalized();
    }

    // Cheap AO: how much higher the neighbourhood sits than this point. 1 = open
    // sky, lower = hollow. Samples 8 directions at radius and radius/2.
    public float Ao(int x, int y, int radius, float strength = 1f)
    {
        float h = At(x, y);
        float occ = 0f;
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            occ += Mathf.Max(0f, At(x + dx * radius, y + dy * radius) - h);
            occ += Mathf.Max(0f, At(x + dx * radius / 2, y + dy * radius / 2) - h) * 0.5f;
            n++;
        }
        return Mathf.Clamp(1f - strength * occ / (n * radius), 0f, 1f);
    }

    private float At(int x, int y)
        => _h[Mathf.Clamp(y, 0, Height - 1) * Width + Mathf.Clamp(x, 0, Width - 1)];
}
