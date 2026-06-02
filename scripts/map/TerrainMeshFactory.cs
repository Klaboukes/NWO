using System.Collections.Generic;
using Godot;

namespace NWO.Map;

// Builds one hex-prism Mesh per TerrainType for the 3D world (Phase 7 V7.1/V7.2).
// Each prism is a flat-top hexagon top face raised to HexProjection.TopHeight,
// with six cliff side walls dropping to the ground plane (Y = 0). The mesh has two
// surfaces:
//   • top face — UV-mapped and textured per terrain (V7.2) via TerrainTextureRegistry
//     (a real PNG when present, else a synthesized dappled tile). Faint vertex shading
//     multiplies over the texture for a little edge form.
//   • cliffs   — vertex-coloured darkened copy of the terrain colour, lit by the
//     DirectionalLight so the prism reads as a tilted 3D board. Cliffs are real
//     geometry, so they need no art (geometry-only per the roadmap).
//
// The geometry contract (top hex at TopHeight, cliffs to ground) stays fixed so
// picking, animation, and billboard anchoring keep working; only the top-face
// material/UVs changed in V7.2.
public sealed class TerrainMeshFactory
{
    private const float Inset = 1f; // small gap between adjacent tiles

    private readonly Dictionary<(TerrainType Terrain, bool Hill), Mesh> _meshes = new();
    private readonly TerrainTextureRegistry _textures = new();

    // Shared cliff material: vertex-colour albedo so the darkened side walls shade.
    private readonly StandardMaterial3D _cliffMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness              = 0.95f,
        Metallic               = 0f,
        TextureFilter          = BaseMaterial3D.TextureFilterEnum.Nearest, // crisp pixel-art
    };

    // A Hills feature raises the same textured prism higher, so a hilly tile reads as
    // a bump of its own biome (Grassland + Hills = a raised grassland tile).
    public Mesh For(TerrainType terrain, bool hill = false)
    {
        var key = (terrain, hill);
        if (_meshes.TryGetValue(key, out var mesh)) return mesh;
        mesh = Build(terrain, hill);
        _meshes[key] = mesh;
        return mesh;
    }

    private Mesh Build(TerrainType terrain, bool hill)
    {
        float size  = HexProjection.HexSize - Inset;
        float topY  = HexProjection.TopHeight(terrain, hill);
        Color cliff = new(HexProjection.TerrainColor(terrain).R * 0.5f,
                          HexProjection.TerrainColor(terrain).G * 0.5f,
                          HexProjection.TerrainColor(terrain).B * 0.5f);

        // Faint vertex shading multiplied over the top texture: white centre, gently
        // darkened rim so the tile gets a touch of edge form (texture stays dominant).
        var centreCol = Colors.White;
        var rimCol    = new Color(0.9f, 0.9f, 0.9f);

        // ── Surface 0: textured top face (UV-mapped fan of 6 triangles) ──
        var topSt = new SurfaceTool();
        topSt.Begin(Mesh.PrimitiveType.Triangles);
        var centre = new Vector3(0f, topY, 0f);
        for (int i = 0; i < 6; i++)
        {
            var a = HexProjection.Corner(i,           size) + new Vector3(0f, topY, 0f);
            var b = HexProjection.Corner((i + 1) % 6, size) + new Vector3(0f, topY, 0f);
            AddTopTri(topSt, centre, a, b, size, centreCol, rimCol, rimCol);
        }
        topSt.SetMaterial(_textures.Material(terrain));
        var mesh = topSt.Commit();

        // ── Surface 1: cliff side walls (vertex-coloured, untextured) ──
        var cliffSt = new SurfaceTool();
        cliffSt.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < 6; i++)
        {
            var tA = HexProjection.Corner(i,           size) + new Vector3(0f, topY, 0f);
            var tB = HexProjection.Corner((i + 1) % 6, size) + new Vector3(0f, topY, 0f);
            var bA = new Vector3(tA.X, 0f, tA.Z);
            var bB = new Vector3(tB.X, 0f, tB.Z);
            // Outward normal: horizontal, pointing away from the tile centre.
            var n  = new Vector3((tA.X + tB.X) * 0.5f, 0f, (tA.Z + tB.Z) * 0.5f).Normalized();
            AddTri(cliffSt, n, tA, bA, bB, cliff, cliff, cliff);
            AddTri(cliffSt, n, tA, bB, tB, cliff, cliff, cliff);
        }
        cliffSt.SetMaterial(_cliffMaterial);
        return cliffSt.Commit(mesh); // append as a second surface on the same mesh
    }

    // Top-face triangle with UVs: a square texture is mapped onto the hexagon via a
    // circumscribed-square projection — local (x, _, z) in [-size, size] → uv [0, 1]
    // (centre → 0.5,0.5). Corners that fall outside the hex footprint of a hex-shaped
    // texture stay transparent/unused, which is fine for tile art authored as a hex.
    private static void AddTopTri(
        SurfaceTool st,
        Vector3 p0, Vector3 p1, Vector3 p2,
        float size, Color c0, Color c1, Color c2)
    {
        st.SetNormal(Vector3.Up); st.SetUV(Uv(p0, size)); st.SetColor(c0); st.AddVertex(p0);
        st.SetNormal(Vector3.Up); st.SetUV(Uv(p1, size)); st.SetColor(c1); st.AddVertex(p1);
        st.SetNormal(Vector3.Up); st.SetUV(Uv(p2, size)); st.SetColor(c2); st.AddVertex(p2);
    }

    private static Vector2 Uv(Vector3 p, float size) =>
        new(0.5f + p.X / (2f * size), 0.5f + p.Z / (2f * size));

    private static void AddTri(
        SurfaceTool st, Vector3 normal,
        Vector3 p0, Vector3 p1, Vector3 p2,
        Color c0, Color c1, Color c2)
    {
        st.SetNormal(normal); st.SetColor(c0); st.AddVertex(p0);
        st.SetNormal(normal); st.SetColor(c1); st.AddVertex(p1);
        st.SetNormal(normal); st.SetColor(c2); st.AddVertex(p2);
    }
}
