using System.Collections.Generic;
using Godot;

namespace NWO.Map;

// Builds one hex-prism Mesh per TerrainType for the 3D world (Phase 7 V7.1).
// Each prism is a flat-top hexagon top face raised to HexProjection.TopHeight,
// with six cliff side walls dropping to the ground plane (Y = 0). Vertex colours
// carry the look: the top face uses the terrain colour (with a brighter rim), the
// cliffs a darkened copy — so the map reads as a tilted 3D board on zero committed
// art. A single shared material (vertex-colour albedo) lets the DirectionalLight
// shade the cliffs for form.
//
// Placeholder-first (mirrors AudioManager + the old tile pipeline): real pixel-art
// top-face textures drop in at V7.2 by overriding the top surface's material; the
// geometry contract here (top hex at TopHeight, cliffs to ground) stays fixed.
public sealed class TerrainMeshFactory
{
    private const float Inset = 1f; // small gap between adjacent tiles

    private readonly Dictionary<TerrainType, Mesh> _meshes = new();
    private readonly StandardMaterial3D _material = new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness              = 0.95f,
        Metallic               = 0f,
        TextureFilter          = BaseMaterial3D.TextureFilterEnum.Nearest, // crisp pixel-art
    };

    public Mesh For(TerrainType terrain)
    {
        if (_meshes.TryGetValue(terrain, out var mesh)) return mesh;
        mesh = Build(terrain);
        _meshes[terrain] = mesh;
        return mesh;
    }

    private Mesh Build(TerrainType terrain)
    {
        float size  = HexProjection.HexSize - Inset;
        float topY  = HexProjection.TopHeight(terrain);
        Color top   = HexProjection.TerrainColor(terrain);
        Color rim   = new(top.R * 1.15f, top.G * 1.15f, top.B * 1.15f);
        Color cliff = new(top.R * 0.5f,  top.G * 0.5f,  top.B * 0.5f);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Top face: fan of 6 triangles from the centre. Centre slightly brighter
        // (rim) so tiles don't read as flat single-colour blobs.
        var centre = new Vector3(0f, topY, 0f);
        for (int i = 0; i < 6; i++)
        {
            var a = HexProjection.Corner(i,           size) + new Vector3(0f, topY, 0f);
            var b = HexProjection.Corner((i + 1) % 6, size) + new Vector3(0f, topY, 0f);
            AddTri(st, Vector3.Up, centre, a, b, rim, top, top);
        }

        // Cliff side walls: a quad per edge from the top hexagon down to Y = 0.
        for (int i = 0; i < 6; i++)
        {
            var tA = HexProjection.Corner(i,           size) + new Vector3(0f, topY, 0f);
            var tB = HexProjection.Corner((i + 1) % 6, size) + new Vector3(0f, topY, 0f);
            var bA = new Vector3(tA.X, 0f, tA.Z);
            var bB = new Vector3(tB.X, 0f, tB.Z);
            // Outward normal: horizontal, pointing away from the tile centre.
            var n  = new Vector3((tA.X + tB.X) * 0.5f, 0f, (tA.Z + tB.Z) * 0.5f).Normalized();
            AddTri(st, n, tA, bA, bB, cliff, cliff, cliff);
            AddTri(st, n, tA, bB, tB, cliff, cliff, cliff);
        }

        st.SetMaterial(_material);
        return st.Commit();
    }

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
