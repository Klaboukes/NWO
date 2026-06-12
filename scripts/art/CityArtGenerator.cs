using Godot;
using NWO.Art.Cities;
using NWO.Art.Units;

namespace NWO.Art;

// Procedural painterly generator for NWO city billboard sprites (Phase 7 V7.5).
//
// 256px full-colour walled settlement — depth-layered houses, keep, curtain
// wall, towers (see CityPainter) — finished with the shared unit post-pass
// chain so cities and units read as one art family. The owner colour rides the
// separate banner sprite, never a body tint.
//
// Two variants: regular city, and the capital (taller keep, gold roof, pennant).
// Deterministic per variant; a real PNG at assets/art/cities/ overrides it.
public static class CityArtGenerator
{
    public const int TileSize = UnitPaintContext.Size;

    public static Image Generate(bool isCapital)
    {
        var ctx = new UnitPaintContext(isCapital ? "city-capital" : "city");
        CityPainter.Draw(ctx, isCapital);
        return ctx.Canvas.ToImage();
    }
}
