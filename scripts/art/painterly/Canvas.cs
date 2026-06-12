using Godot;

namespace NWO.Art.Painterly;

// Float-RGBA working surface (straight alpha). All painting happens here in
// managed memory so the library stays unit-testable headless; ToImage() is the
// single Godot.Image touchpoint, called once per finished asset.
public sealed class Canvas
{
    private readonly float[] _px; // straight-alpha RGBA, row-major

    public Canvas(int size) : this(size, size) { }

    public Canvas(int width, int height)
    {
        Width  = width;
        Height = height;
        _px    = new float[width * height * 4];
    }

    public int Width  { get; }
    public int Height { get; }

    public Color Get(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new Color(_px[i], _px[i + 1], _px[i + 2], _px[i + 3]);
    }

    public float Alpha(int x, int y) => _px[(y * Width + x) * 4 + 3];

    public void Set(int x, int y, Color c)
    {
        int i = (y * Width + x) * 4;
        _px[i] = c.R; _px[i + 1] = c.G; _px[i + 2] = c.B; _px[i + 3] = c.A;
    }

    public void Fill(Color c)
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            Set(x, y, c);
    }

    // Source-over: paint c (scaled by alpha) on top of what's there.
    public void Blend(int x, int y, Color c, float alpha)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        float sa = c.A * alpha;
        if (sa <= 0f) return;
        int i = (y * Width + x) * 4;
        float da = _px[i + 3];
        float oa = sa + da * (1f - sa);
        if (oa <= 0f) return;
        float inv = 1f / oa;
        _px[i]     = (c.R * sa + _px[i]     * da * (1f - sa)) * inv;
        _px[i + 1] = (c.G * sa + _px[i + 1] * da * (1f - sa)) * inv;
        _px[i + 2] = (c.B * sa + _px[i + 2] * da * (1f - sa)) * inv;
        _px[i + 3] = oa;
    }

    // Destination-over: paint c UNDER what's there (contact shadows, dark rims —
    // anything that must sit behind already-painted bodywork).
    public void BlendUnder(int x, int y, Color c, float alpha)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        float sa = c.A * alpha;
        if (sa <= 0f) return;
        int i = (y * Width + x) * 4;
        float da = _px[i + 3];
        float oa = da + sa * (1f - da);
        if (oa <= 0f) return;
        float inv = 1f / oa;
        _px[i]     = (_px[i]     * da + c.R * sa * (1f - da)) * inv;
        _px[i + 1] = (_px[i + 1] * da + c.G * sa * (1f - da)) * inv;
        _px[i + 2] = (_px[i + 2] * da + c.B * sa * (1f - da)) * inv;
        _px[i + 3] = oa;
    }

    // Source-over compose an entire layer on top of this one.
    public void Compose(Canvas layer, float opacity = 1f)
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            Blend(x, y, layer.Get(x, y), opacity);
    }

    // Multiply RGB by the mask's RGB (alpha untouched) — AO / shadow passes.
    public void Multiply(Canvas mask)
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int i = (y * Width + x) * 4;
            var m = mask.Get(x, y);
            _px[i] *= m.R; _px[i + 1] *= m.G; _px[i + 2] *= m.B;
        }
    }

    // Scale RGB by a scalar at one pixel (edge AO, vignettes).
    public void ScaleRgb(int x, int y, float f)
    {
        int i = (y * Width + x) * 4;
        _px[i] *= f; _px[i + 1] *= f; _px[i + 2] *= f;
    }

    // The single Godot.Image touchpoint. Opaque assets (terrain) bake to Rgb8;
    // sprites get an AlphaBleed pass first so Linear filtering never pulls black
    // from fully-transparent neighbours (the halo fix), then bake to Rgba8.
    public Image ToImage(bool opaque = false)
    {
        if (!opaque) SpriteFx.AlphaBleed(this);
        var img = Image.CreateEmpty(Width, Height, false,
                                    opaque ? Image.Format.Rgb8 : Image.Format.Rgba8);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var c = Get(x, y);
            img.SetPixel(x, y, opaque ? new Color(c.R, c.G, c.B) : c);
        }
        return img;
    }
}
