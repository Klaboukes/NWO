using Godot;
using NWO.Art.Units;

namespace NWO.Art;

// Procedural painterly generator for NWO unit billboard sprites (Phase 7 V7.5).
//
// STYLE  Full-colour 256px characters in the shared painterly family: one
// parametric figure (HumanoidPainter) dressed per unit via material ramps
// (MaterialRamps — skin/leather/steel/cloth/wood), plus ships, vehicles and a
// mount, all lit by the same upper-left sun as the terrain. Finished with
// crevice AO, a cool rim light, a soft dark outline (reads on any terrain) and
// a contact shadow. RGBA8 transparent background, alpha-bled for Linear filtering.
//
// TEAM COLOUR  Sprites are NOT owner-tinted — WorldRenderer always shows the
// owner-coloured banner beside the body (docs/ART_ASSETS.md "team-colour banners").
//
// DETERMINISM  Generate(unitId) is pure — same input → same Image bytes (the RNG
// seeds from an FNV hash of the id). Unknown ids fall back to a shaded disc token
// so new content never crashes the renderer.
public static class UnitArtGenerator
{
    public const int TileSize = UnitPaintContext.Size;

    public static Image Generate(string unitId)
    {
        var ctx = new UnitPaintContext(unitId);
        UnitCatalog.Draw(ctx, unitId);
        return ctx.Canvas.ToImage();
    }
}
