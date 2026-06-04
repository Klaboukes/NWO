namespace NWO.Map;

public enum MapScript { Continents, Pangaea, Archipelago, Highlands }

public enum MapSize { Small, Standard, Large }

// Tunable parameters that define a map script's continental shape, sea level, and
// mountain character. Climate (moisture/temperature) and resource layers are shared
// across all scripts (see docs/MAP_GENERATION.md).
public record MapScriptParams(
    float RadialFalloff,
    float BaseFrequency,
    float DetailFrequency,
    float OceanLevel,
    float CoastLevel,
    float MountainBoost,
    float MountainLevel,
    float UpliftFrequency,
    float UpliftLow,
    float UpliftHigh,
    float HillRelief,
    float HillFrequency,
    float HillThreshold,
    float TargetLandPercent)  // percentile trick: land fraction the generator targets
{
    private static readonly MapScriptParams Continents = new(
        RadialFalloff:     0.33f,
        BaseFrequency:     0.045f,
        DetailFrequency:   0.11f,
        OceanLevel:        0.25f,
        CoastLevel:        0.30f,
        MountainBoost:     0.55f,
        MountainLevel:     0.72f,
        UpliftFrequency:   0.03f,
        UpliftLow:         0.45f,
        UpliftHigh:        0.80f,
        HillRelief:        0.52f,
        HillFrequency:     0.14f,
        HillThreshold:     0.68f,
        TargetLandPercent: 0.35f);

    // One large supercontinent — low radial falloff (edges stay dry) + wide noise scale.
    private static readonly MapScriptParams Pangaea = Continents with {
        RadialFalloff     = 0.18f,
        BaseFrequency     = 0.025f,
        OceanLevel        = 0.22f,
        TargetLandPercent = 0.50f,
    };

    // Many small islands — high falloff (aggressive ocean push) + tight noise scale.
    private static readonly MapScriptParams Archipelago = Continents with {
        RadialFalloff     = 0.50f,
        BaseFrequency     = 0.085f,
        OceanLevel        = 0.32f,
        MountainBoost     = 0.35f,
        HillThreshold     = 0.70f,
        TargetLandPercent = 0.22f,
    };

    // Mountain-heavy interior — higher boost + lower thresholds + wider uplift belts.
    private static readonly MapScriptParams Highlands = Continents with {
        MountainBoost   = 0.85f,
        MountainLevel   = 0.60f,
        UpliftFrequency = 0.025f,
        UpliftLow       = 0.30f,
        HillRelief      = 0.42f,
        HillThreshold   = 0.55f,
    };

    public static MapScriptParams For(MapScript script) => script switch
    {
        MapScript.Pangaea     => Pangaea,
        MapScript.Archipelago => Archipelago,
        MapScript.Highlands   => Highlands,
        _                     => Continents,
    };
}
