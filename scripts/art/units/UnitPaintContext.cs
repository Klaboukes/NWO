using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Units;

// Per-sprite working state for unit painting (Phase 7 V7.5): a 256px transparent
// canvas, painter, and an RNG seeded deterministically from the unit id — same id,
// same bytes, so the runtime placeholder matches any baked PNG.
//
// Finish() is the shared post-pass chain that makes every unit read as one art
// family: crevice AO, a cool rim on the shadow side, a soft dark outline so the
// sprite reads on any terrain, and a contact shadow grounding it.
public sealed class UnitPaintContext
{
    public const int Size = 256;

    public UnitPaintContext(string unitId)
    {
        Canvas  = new Canvas(Size);
        Painter = new Painter(Canvas);
        Rng     = new Rng(Fnv1A(unitId));
    }

    public Canvas Canvas { get; }
    public Painter Painter { get; }
    public Rng Rng { get; }

    public void Finish(Vector2 groundCentre, Vector2 shadowRadii, float shadowStrength = 0.42f)
    {
        SpriteFx.AmbientOcclusionFromAlpha(Canvas, radius: 3, strength: 0.20f);
        SpriteFx.RimLight(Canvas, new Vector2(0.7f, 0.7f),
                          new Color(0.75f, 0.85f, 1f, 1f), width: 2.5f, strength: 0.30f);
        SpriteFx.DarkRim(Canvas, new Color(0.09f, 0.08f, 0.10f, 1f), width: 2.2f);
        SpriteFx.ContactShadow(Canvas, groundCentre, shadowRadii, shadowStrength);
    }

    // Deterministic 64-bit FNV-1a over the unit id — no GetHashCode (randomized
    // per process), no time. Same id, same seed, forever.
    private static ulong Fnv1A(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h;
    }
}
