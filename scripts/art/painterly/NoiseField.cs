using Godot;

namespace NWO.Art.Painterly;

// Seed-keyed value noise + fractal stacks. All functions are pure in (x, y, seed)
// — no state — so any painter can sample the same field deterministically.
// Coordinates are in "cells": callers divide pixel coords by their feature scale.
// (Named NoiseField because Godot already ships a `Noise` resource class.)
public static class NoiseField
{
    // Cheap deterministic integer hash → [0,1]. Stable for a given (x, y, seed).
    public static float Hash01(int x, int y, int seed)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }

    // Smooth bilinear value noise over the integer lattice, [0,1].
    public static float Value(float x, float y, int seed)
    {
        int   x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = Smooth(x - x0),      fy = Smooth(y - y0);

        float v00 = Hash01(x0,     y0,     seed), v10 = Hash01(x0 + 1, y0,     seed);
        float v01 = Hash01(x0,     y0 + 1, seed), v11 = Hash01(x0 + 1, y0 + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
    }

    // Fractal Brownian motion: stacked value-noise octaves, normalised to [0,1].
    public static float Fbm(float x, float y, int seed, int octaves = 4,
                            float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0f, amp = 1f, norm = 0f, fx = x, fy = y;
        for (int o = 0; o < octaves; o++)
        {
            sum  += amp * Value(fx, fy, seed + o * 101);
            norm += amp;
            amp  *= gain;
            fx   *= lacunarity;
            fy   *= lacunarity;
        }
        return sum / norm;
    }

    // Ridged fBm: sharp crests where the noise crosses its midline — mountain
    // ridgelines, dune crests. [0,1], 1 at the ridges.
    public static float Ridged(float x, float y, int seed, int octaves = 4)
    {
        float sum = 0f, amp = 1f, norm = 0f, fx = x, fy = y;
        for (int o = 0; o < octaves; o++)
        {
            float n = 1f - Mathf.Abs(2f * Value(fx, fy, seed + o * 101) - 1f);
            sum  += amp * n * n; // squaring sharpens the crest
            norm += amp;
            amp  *= 0.5f;
            fx   *= 2f;
            fy   *= 2f;
        }
        return sum / norm;
    }

    // Domain-warp offset: a pseudo-vector field that bends straight noise features
    // into organic meanders. Add the returned offset to the sample coordinates.
    public static Vector2 Warp(float x, float y, int seed, float amp)
    {
        float ox = Fbm(x, y, seed + 7331, 3) - 0.5f;
        float oy = Fbm(x, y, seed + 1337, 3) - 0.5f;
        return new Vector2(ox, oy) * (2f * amp);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
