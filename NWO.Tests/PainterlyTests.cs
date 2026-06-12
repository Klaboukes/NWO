using Godot;
using NWO.Art.Painterly;
using Xunit;

namespace NWO.Tests;

// Guards the painterly art library (Phase 7 V7.5). Everything here runs headless:
// the library is managed-types-only by design (Canvas.ToImage is the single
// Godot.Image touchpoint and is exercised by the bake tool, not by tests).
// The key contract is determinism — same seed, same bytes — because baked PNGs
// are committed to git and runtime placeholders must match them.
public class PainterlyTests
{
    [Fact]
    public void Rng_SameSeed_SameSequence()
    {
        var a = new Rng(12345);
        var b = new Rng(12345);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.Float(), b.Float());
    }

    [Fact]
    public void Rng_OutputsStayInRange()
    {
        var rng = new Rng(99);
        for (int i = 0; i < 200; i++)
        {
            float f = rng.Float();
            Assert.InRange(f, 0f, 0.9999999f);
            int n = rng.Range(3, 17);
            Assert.InRange(n, 3, 16);
            float r = rng.Range(-2f, 5f);
            Assert.InRange(r, -2f, 5f);
            Assert.True(rng.InUnitDisc().Length() <= 1.0001f);
        }
    }

    [Fact]
    public void Noise_IsDeterministicAndBounded()
    {
        for (int i = 0; i < 50; i++)
        {
            float x = i * 0.37f, y = i * 0.91f;
            Assert.Equal(NoiseField.Fbm(x, y, 7), NoiseField.Fbm(x, y, 7));
            Assert.InRange(NoiseField.Fbm(x, y, 7), 0f, 1f);
            Assert.InRange(NoiseField.Value(x, y, 7), 0f, 1f);
            Assert.InRange(NoiseField.Ridged(x, y, 7), 0f, 1f);
        }
    }

    [Fact]
    public void Sdf_Circle_SignedDistanceIsExact()
    {
        var c = new Vector2(10f, 10f);
        Assert.Equal(-5f, Sdf.Circle(new Vector2(10f, 10f), c, 5f), 3);
        Assert.Equal(0f,  Sdf.Circle(new Vector2(15f, 10f), c, 5f), 3);
        Assert.Equal(5f,  Sdf.Circle(new Vector2(20f, 10f), c, 5f), 3);
    }

    [Fact]
    public void Sdf_Hexagon_CentreIsApothemDeep()
    {
        // Flat-top hex, circumradius 64: centre sits one apothem inside the edge.
        float d = Sdf.Hexagon(new Vector2(64f, 64f), new Vector2(64f, 64f), 64f);
        Assert.Equal(-0.8660254f * 64f, d, 2);
    }

    [Fact]
    public void Sdf_Polygon_SignMatchesInterior()
    {
        var tri = new[] { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(5f, 10f) };
        Assert.True(Sdf.Polygon(new Vector2(5f, 3f), tri) < 0f);   // inside
        Assert.True(Sdf.Polygon(new Vector2(-5f, -5f), tri) > 0f); // outside
    }

    [Fact]
    public void ColorRamp_SamplesEndpointsAndCentre()
    {
        var baseCol = new Color(0.4f, 0.6f, 0.3f);
        var ramp = ColorRamp.Painterly(baseCol);
        Assert.Equal(baseCol, ramp.Sample(0.5f));
        // Shadow end is darker than the highlight end.
        Assert.True(ramp.Sample(0f).V < ramp.Sample(1f).V);
        // Clamped outside [0,1].
        Assert.Equal(ramp.Sample(0f), ramp.Sample(-3f));
        Assert.Equal(ramp.Sample(1f), ramp.Sample(42f));
    }

    [Fact]
    public void HeightField_Normal_TiltsAgainstSlope()
    {
        var hf = new HeightField(8, 8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            hf[x, y] = x; // rises toward +x
        var n = hf.Normal(4, 4, 1f);
        Assert.True(n.X < 0f);              // normal leans away from the rise
        Assert.Equal(0f, n.Y, 3);
        Assert.Equal(1f, n.Length(), 3);
    }

    [Fact]
    public void Canvas_SrcOverBlend_MatchesCompositingMath()
    {
        var c = new Canvas(2, 2);
        c.Set(0, 0, new Color(0f, 0f, 1f, 1f));            // opaque blue
        c.Blend(0, 0, new Color(1f, 0f, 0f, 1f), 0.5f);    // half-alpha red over it
        var r = c.Get(0, 0);
        Assert.Equal(0.5f, r.R, 3);
        Assert.Equal(0.5f, r.B, 3);
        Assert.Equal(1f,   r.A, 3);
    }

    [Fact]
    public void Canvas_BlendUnder_NeverCoversOpaquePixels()
    {
        var c = new Canvas(2, 2);
        c.Set(0, 0, new Color(1f, 0f, 0f, 1f));            // opaque red body
        c.BlendUnder(0, 0, new Color(0f, 0f, 0f, 1f), 1f); // shadow underneath
        Assert.Equal(new Color(1f, 0f, 0f, 1f), c.Get(0, 0));
        // ...but fills transparent pixels.
        c.BlendUnder(1, 1, new Color(0f, 1f, 0f, 1f), 0.5f);
        Assert.Equal(0.5f, c.Get(1, 1).A, 3);
    }

    [Fact]
    public void Lighting_FlatNormalLitBetweenExtremes()
    {
        var flat = new Vector3(0f, 0f, 1f);
        float lit = Lighting.Lambert(flat);
        Assert.InRange(lit, 0.3f, 1f);
        // A normal facing the sun is brighter than one facing away.
        Assert.True(Lighting.Lambert(Lighting.SunDir) > Lighting.Lambert(-Lighting.SunDir));
    }

    [Fact]
    public void HexTile_EdgeDistance_PositiveInsideNegativeOutside()
    {
        Assert.True(HexTile.EdgeDistance(128f, 128f, 256) > 0f);  // centre
        Assert.True(HexTile.EdgeDistance(0f, 0f, 256) < 0f);      // square corner, off-hex
        Assert.True(HexTile.InFootprint(128f, 128f, 256, 40f));
    }

    // The cross-stage determinism guard: a small managed-only render must hash
    // identically on every run, or committed bakes would churn in git.
    [Fact]
    public void Painter_SmallRender_IsDeterministic()
    {
        string Render()
        {
            var canvas = new Canvas(32, 32);
            var painter = new Painter(canvas);
            var ramp = ColorRamp.Painterly(new Color(0.5f, 0.4f, 0.3f));
            painter.FillShaded(p => Sdf.Circle(p, new Vector2(16f, 16f), 10f),
                               new Rect2(6f, 6f, 20f, 20f), ramp, 6f, specular: 0.5f);
            SpriteFx.DarkRim(canvas, new Color(0.1f, 0.1f, 0.1f, 1f), 2f);
            SpriteFx.ContactShadow(canvas, new Vector2(16f, 26f), new Vector2(10f, 4f), 0.4f);

            var sb = new System.Text.StringBuilder();
            for (int y = 0; y < 32; y += 3)
            for (int x = 0; x < 32; x += 3)
            {
                var c = canvas.Get(x, y);
                sb.Append($"{c.R:F4},{c.G:F4},{c.B:F4},{c.A:F4};");
            }
            return sb.ToString();
        }

        Assert.Equal(Render(), Render());
    }

    [Fact]
    public void Painter_FillShaded_LightsUpperLeftBrighter()
    {
        // The shared sun sits upper-left: a pillow-shaded sphere must be brighter
        // on its upper-left flank than its lower-right.
        var canvas = new Canvas(32, 32);
        var painter = new Painter(canvas);
        painter.FillShaded(p => Sdf.Circle(p, new Vector2(16f, 16f), 12f),
                           new Rect2(4f, 4f, 24f, 24f),
                           ColorRamp.Painterly(new Color(0.5f, 0.5f, 0.5f)), 12f);
        Assert.True(canvas.Get(10, 10).V > canvas.Get(22, 22).V);
    }
}
