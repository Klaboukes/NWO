using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Icons;

// Shared finish for the 64px icon family (resources + HUD): a touch of
// alpha-AO to settle the forms and a soft dark rim so every icon reads on any
// terrain or panel. Kept identical across both generators so the whole icon
// set looks like one hand.
public static class IconFx
{
    public static void Finish(Canvas canvas)
    {
        SpriteFx.AmbientOcclusionFromAlpha(canvas, radius: 2, strength: 0.16f);
        SpriteFx.DarkRim(canvas, new Color(0.10f, 0.09f, 0.11f, 1f), width: 1.6f);
    }
}
