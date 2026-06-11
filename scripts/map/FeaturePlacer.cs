using System.Collections.Generic;
using Godot;

namespace NWO.Map;

// Per-tile climate inputs the feature placer reads. Sampled by MapGenerator's noise
// pass and handed over as plain data, so the placer itself never touches
// FastNoiseLite and xUnit can drive it with synthetic fields.
public readonly record struct ClimateSample(
    float Moisture,    // 0..1, independent low-freq noise axis
    float Temperature, // 0..1, latitudinal gradient + jitter
    float Latitude,    // 0 equator → 1 pole
    float ForestNoise);// 0..1, mid-freq clump field dedicated to forest growth

// The Civ5-style AddFeatures pass (Phase 14.3): vegetation and ice are placed on the
// FINAL terrain (after lakes/coasts/rivers), in order Ice → Jungle → Forest → Marsh
// → Oasis so the equatorial jungle band claims its tiles before forest can. Every
// placement is validated against FeatureRules; everything is deterministic per seed
// (per-tile hashes, no shared RNG stream).
public static class FeaturePlacer
{
    // Ice: permanent pack above PolarIceLat; a hash-rolled ramp up to it.
    private const float PolarIceLat   = 0.92f;
    private const float IceRampLat    = 0.85f;
    private const float IceRampChance = 0.6f; // chance multiplier at the ramp's top

    // Jungle: the hot, wet equatorial band.
    private const float JungleMaxLat      = 0.22f;
    private const float JungleMinMoisture = 0.55f;

    // Forest: clumped growth — see the threshold blend in PlaceForest.
    private const float TundraForestPenalty = 0.05f; // sparser boreal woods

    // Marsh: rare pockets on the wettest flat grassland.
    private const float MarshMinMoisture = 0.66f;
    private const float MarshChance      = 0.30f;

    // Oasis: rare, isolated springs on open desert away from any other water.
    private const float OasisChance = 0.05f;

    public static void Place(MapData data, IReadOnlyDictionary<Vector2I, ClimateSample> climate,
        MapScriptParams p, int seed)
    {
        foreach (var (axial, terrain) in data.Tiles)
        {
            if (!climate.TryGetValue(axial, out var c)) continue;
            var mask = data.FeatureAt(axial);

            var add = Pick(data, axial, terrain, mask, c, p, seed);
            if (add == Feature.None) continue;

            mask |= add;
            if (FeatureRules.IsLegal(terrain, mask)) data.Features[axial] = mask;
        }
    }

    // The single vegetation/overlay flag this tile earns (at most one — FeatureRules
    // allows only one besides Hills). Order matters: Ice owns the polar sea, Jungle
    // owns the equator, Forest fills the temperate clumps, then the rare accents.
    private static Feature Pick(MapData data, Vector2I axial, TerrainType terrain,
        Feature mask, ClimateSample c, MapScriptParams p, int seed)
    {
        bool flat = (mask & Feature.Hills) == 0;

        // Ice — sea water only (a Lake never freezes over; it must stay workable).
        if (TerrainYields.IsSeaWater(terrain))
        {
            if (c.Latitude > PolarIceLat) return Feature.Ice;
            if (c.Latitude > IceRampLat
                && Hash(axial, seed, 1) < (c.Latitude - IceRampLat) / (PolarIceLat - IceRampLat) * IceRampChance)
                return Feature.Ice;
            return Feature.None;
        }
        if (TerrainYields.IsWater(terrain) || terrain == TerrainType.Mountain) return Feature.None;

        // Jungle — the equatorial band.
        if (c.Latitude < JungleMaxLat && c.Moisture > JungleMinMoisture
            && terrain is TerrainType.Grassland or TerrainType.Plains)
            return Feature.Jungle;

        // Forest — coherent clumps where the dedicated noise field and moisture
        // agree. Boreal (Tundra) woods need a slightly stronger signal.
        if (terrain is TerrainType.Grassland or TerrainType.Plains or TerrainType.Tundra)
        {
            float threshold = p.ForestThreshold + (terrain == TerrainType.Tundra ? TundraForestPenalty : 0f);
            if (0.55f * c.ForestNoise + 0.45f * c.Moisture > threshold)
                return Feature.Forest;
        }

        // Marsh — wettest flat grassland.
        if (flat && terrain == TerrainType.Grassland && c.Moisture > MarshMinMoisture
            && Hash(axial, seed, 2) < MarshChance)
            return Feature.Marsh;

        // Oasis — open flat desert, isolated from rivers, water, and other oases.
        if (flat && terrain == TerrainType.Desert && Hash(axial, seed, 3) < OasisChance
            && !data.IsRiverAdjacent(axial) && !NextToWaterOrOasis(data, axial))
            return Feature.Oasis;

        return Feature.None;
    }

    private static bool NextToWaterOrOasis(MapData data, Vector2I axial)
    {
        foreach (var n in HexGrid.GetNeighbors(axial))
        {
            if (data.Tiles.TryGetValue(n, out var nt) && TerrainYields.IsWater(nt)) return true;
            if (data.HasFeature(n, Feature.Oasis)) return true;
        }
        return false;
    }

    // Deterministic per-tile hash in [0,1), salted per decision so the ice, marsh,
    // and oasis rolls are independent of each other.
    private static float Hash(Vector2I axial, int seed, int salt)
        => MapPostProcess.Hash01(axial, seed * 31 + salt);
}
