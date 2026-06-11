using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NWO.Entities;
using NWO.Map;

namespace NWO.Core;

// One Civilopedia entry: a title and a fully-rendered detail body the UI shows verbatim.
public record CivilopediaEntry(string Id, string Title, string Detail);

// A named group of entries (Factions, Units, Terrain, …) shown as one tab/section.
public record CivilopediaCategory(string Id, string Name, IReadOnlyList<CivilopediaEntry> Entries);

// Headless assembly of the in-game Civilopedia. Combines live game data (DataCatalog,
// the terrain/resource enums + TerrainYields) with authored prose (CivilopediaContent)
// into an ordered list of categories the UI renders without further logic. Catalog-backed
// entries show a stats block built from current data + prose looked up by key (so they
// never go stale and degrade gracefully when prose is missing); "world"/"mechanics"
// articles come straight from the content file. No Godot dependency — unit-testable.
public class CivilopediaService
{
    private readonly DataCatalog        _catalog;
    private readonly CivilopediaContent _content;

    public IReadOnlyList<CivilopediaCategory> Categories { get; }

    public CivilopediaService(DataCatalog catalog, CivilopediaContent? content = null)
    {
        _catalog = catalog;
        _content = content ?? CivilopediaContent.Empty;
        Categories = BuildCategories();
    }

    public static CivilopediaService Load()
        => new(DataCatalog.Load(), DataLoader.LoadCivilopedia());

    private IReadOnlyList<CivilopediaCategory> BuildCategories() => new List<CivilopediaCategory>
    {
        ArticleCategory("world",     "The World"),
        Cat("factions",  "Factions",     _catalog.Factions.Select(FactionEntry)),
        Cat("units",     "Units",        _catalog.Units.Select(UnitEntry)),
        Cat("buildings", "Buildings",    _catalog.Buildings.Select(BuildingEntry)),
        Cat("techs",     "Technologies", _catalog.Techs.Select(TechEntry)),
        Cat("terrain",   "Terrain",      EnumValues<TerrainType>().Select(TerrainEntry)),
        Cat("features",  "Features",     FeatureRules.Flags.Select(FeatureEntry)),
        Cat("resources", "Resources",    EnumValues<ResourceType>().Where(r => r != ResourceType.None).Select(ResourceEntry)),
        ArticleCategory("mechanics", "Game Mechanics"),
    };

    // ── Catalog-backed categories ────────────────────────────────────────────────

    private CivilopediaEntry FactionEntry(FactionData f)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(f.Tree)) sb.AppendLine($"Ideology: {f.Tree}");
        foreach (var line in FactionModifiers(f)) sb.AppendLine("• " + line);
        return Entry($"faction:{f.Id}", f.Name, sb);
    }

    private IEnumerable<string> FactionModifiers(FactionData f)
    {
        if (f.CombatStrengthMult != 1.0) yield return $"{Pct(f.CombatStrengthMult)} combat strength";
        if (f.GoldMult          != 1.0) yield return $"{Pct(f.GoldMult)} gold income";
        if (f.ScienceMult       != 1.0) yield return $"{Pct(f.ScienceMult)} science";
        if (f.RushBuyDiscount   != 0.0) yield return $"-{Math.Round(f.RushBuyDiscount * 100)}% rush-buy cost";
        if (f.SightBonus        != 0)   yield return $"{Signed(f.SightBonus)} sight";
        if (f.TerrainCostMult   != 1.0) yield return $"{Pct(f.TerrainCostMult)} terrain movement cost";
        if (f.SettleCostMult    != 1.0) yield return $"{Pct(f.SettleCostMult)} settler cost";
        if (f.MinCityDistanceDelta != 0)
            yield return $"Cities may be founded {Math.Abs(f.MinCityDistanceDelta)} {Tiles(f.MinCityDistanceDelta)} {(f.MinCityDistanceDelta < 0 ? "closer" : "farther apart")}";
        if (f.CityDefenseBonus  != 0)   yield return $"{Signed(f.CityDefenseBonus)} capital defense";
        if (f.CityRegenMult     != 1.0) yield return $"{Pct(f.CityRegenMult)} capital regeneration";
        if (f.CapitalProductionBonus != 0) yield return $"{Signed(f.CapitalProductionBonus)} capital production";
        if (f.XpGainMult        != 1.0) yield return $"{Pct(f.XpGainMult)} veterancy XP gain";
        if (f.HealInEnemyLand)          yield return "Units heal in enemy territory";
        foreach (var t in f.Traits) yield return TraitText(t);
        foreach (var kv in f.UnitVariants)
        {
            var variant = _catalog.Unit(kv.Value)?.Name ?? kv.Value;
            var baseName = _catalog.Unit(kv.Key)?.Name ?? kv.Key;
            yield return $"Unique unit: {variant} (replaces {baseName})";
        }
    }

    private CivilopediaEntry UnitEntry(UnitData u)
    {
        var sb = new StringBuilder();
        if (u.Attack > 0 || u.Defense > 0) sb.AppendLine($"Strength: {u.Attack} attack / {u.Defense} defense");
        if (u.Range > 1) sb.AppendLine($"Range: {u.Range}");
        sb.AppendLine($"Movement: {u.Movement}    Sight: {u.Sight}");
        sb.AppendLine($"Cost: {u.ProductionCost} production    Upkeep: {u.MaintenanceGold} gold");
        if (u.RequiredTech     != null) sb.AppendLine($"Requires tech: {TechName(u.RequiredTech)}");
        if (u.RequiredResource != null) sb.AppendLine($"Requires resource: {Spaced(Pascal(u.RequiredResource))}");
        if (u.Special == "found_city")       sb.AppendLine("Founds new cities");
        if (u.Special == "build_improvement") sb.AppendLine("Builds tile improvements");
        if (u.IgnoresTerrainCost) sb.AppendLine("Ignores rough-terrain movement costs");
        if (_catalog.IsFactionVariant(u.Id)) sb.AppendLine("Faction unique unit");
        return Entry($"unit:{u.Id}", u.Name, sb);
    }

    private CivilopediaEntry BuildingEntry(BuildingData b)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Cost: {b.ProductionCost} production");
        if (b.RequiredTech != null) sb.AppendLine($"Requires tech: {TechName(b.RequiredTech)}");
        var yields = Yields(b.Yields);
        if (yields.Count > 0) sb.AppendLine("Yields: " + string.Join(", ", yields));
        if (b.Effect != null) sb.AppendLine($"Effect: {EffectText(b.Effect)}");
        return Entry($"building:{b.Id}", b.Name, sb);
    }

    private CivilopediaEntry TechEntry(TechData t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Research cost: {t.ScienceCost} science");
        if (t.Prerequisites.Count > 0)
            sb.AppendLine("Requires: " + string.Join(", ", t.Prerequisites.Select(TechName)));
        var unlocks = t.Unlocks.Units.Select(id => _catalog.Unit(id)?.Name ?? id)
            .Concat(t.Unlocks.Buildings.Select(id => _catalog.Building(id)?.Name ?? id))
            .Concat(t.Unlocks.Improvements.Select(s => Spaced(Pascal(s))))
            .ToList();
        if (unlocks.Count > 0) sb.AppendLine("Unlocks: " + string.Join(", ", unlocks));
        if (t.Unlocks.RevealedResources.Count > 0)
            sb.AppendLine("Reveals: " + string.Join(", ", t.Unlocks.RevealedResources.Select(s => Spaced(Pascal(s)))));
        return Entry($"tech:{t.Id}", t.Name, sb);
    }

    private CivilopediaEntry TerrainEntry(TerrainType t)
    {
        var sb = new StringBuilder();
        var yields = new List<string>();
        if (TerrainYields.Food(t)       != 0) yields.Add($"{TerrainYields.Food(t)} food");
        if (TerrainYields.Production(t)  != 0) yields.Add($"{TerrainYields.Production(t)} production");
        if (TerrainYields.Gold(t)        != 0) yields.Add($"{TerrainYields.Gold(t)} gold");
        sb.AppendLine("Yields: " + (yields.Count > 0 ? string.Join(", ", yields) : "none"));
        int mc = TerrainYields.MovementCost(t);
        sb.AppendLine("Movement cost: " + (mc == int.MaxValue ? "impassable" : mc.ToString()));
        sb.AppendLine(TerrainYields.CanFoundCityOn(t) ? "Cities may be founded here" : "No city center");
        return Entry($"terrain:{Key(t)}", Spaced(t.ToString()), sb);
    }

    // A terrain feature (Forest, Hills, …): yield deltas + movement surcharge +
    // which base terrains it may sit on, all from live data (FeatureYields/
    // FeatureRules) so the entry never goes stale.
    private CivilopediaEntry FeatureEntry(Feature f)
    {
        var sb = new StringBuilder();
        var yields = new List<string>();
        if (FeatureYields.Food(f)       != 0) yields.Add($"{Signed(FeatureYields.Food(f))} food");
        if (FeatureYields.Production(f) != 0) yields.Add($"{Signed(FeatureYields.Production(f))} production");
        if (FeatureYields.Gold(f)       != 0) yields.Add($"{Signed(FeatureYields.Gold(f))} gold");
        sb.AppendLine("Yield deltas: " + (yields.Count > 0 ? string.Join(", ", yields) : "none"));
        if (f == Feature.Ice)
            sb.AppendLine("Blocks ships: sea units cannot enter");
        else if (FeatureYields.MovementCost(f) != 0)
            sb.AppendLine($"Movement cost: +{FeatureYields.MovementCost(f)}");
        var bases = EnumValues<TerrainType>().Where(t => FeatureRules.IsLegal(t, f)).Select(t => Spaced(t.ToString()));
        sb.AppendLine("Found on: " + string.Join(", ", bases));
        return Entry($"feature:{Key(f)}", Spaced(f.ToString()), sb);
    }

    private CivilopediaEntry ResourceEntry(ResourceType r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Class: {ResourceClass(r)} resource");
        return Entry($"resource:{Key(r)}", Spaced(r.ToString()), sb);
    }

    // ── Article (authored-only) categories ───────────────────────────────────────

    private CivilopediaCategory ArticleCategory(string id, string name)
        => new(id, name, _content.Articles.Where(a => a.Category == id)
                                 .Select(a => new CivilopediaEntry(a.Id, a.Title, a.Body)).ToList());

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static CivilopediaCategory Cat(string id, string name, IEnumerable<CivilopediaEntry> entries)
        => new(id, name, entries.ToList());

    // Builds an entry from its stats block, appending authored prose when present.
    private CivilopediaEntry Entry(string key, string title, StringBuilder stats)
    {
        var s = stats.ToString().TrimEnd();
        var prose = _content.Prose.GetValueOrDefault(key, "");
        var detail = string.IsNullOrWhiteSpace(prose) ? s
                   : string.IsNullOrWhiteSpace(s) ? prose
                   : s + "\n\n" + prose;
        return new CivilopediaEntry(key, title, detail);
    }

    private List<string> Yields(BuildingYields y)
    {
        var list = new List<string>();
        if (y.Food       != 0) list.Add($"{Signed(y.Food)} food");
        if (y.Production != 0) list.Add($"{Signed(y.Production)} production");
        if (y.Gold       != 0) list.Add($"{Signed(y.Gold)} gold");
        if (y.Science    != 0) list.Add($"{Signed(y.Science)} science");
        if (y.Culture    != 0) list.Add($"{Signed(y.Culture)} culture");
        return list;
    }

    private string TechName(string id) => _catalog.Tech(id)?.Name ?? Spaced(Pascal(id));

    private static IEnumerable<T> EnumValues<T>() where T : struct, Enum => Enum.GetValues<T>();

    // Signed integer / percentage-delta formatting for the modifier lines.
    private static string Signed(int v) => (v >= 0 ? "+" : "") + v;

    private static string Pct(double mult)
    {
        int p = (int)Math.Round((mult - 1.0) * 100);
        return (p >= 0 ? "+" : "") + p + "%";
    }

    private static string Tiles(int delta) => Math.Abs(delta) == 1 ? "tile" : "tiles";

    private static string TraitText(string trait) => trait switch
    {
        "new_city_defender" => "New cities are founded with a free defender",
        "can_hire_reavers"  => "Can hire Reaver mercenaries for gold",
        _                   => trait,
    };

    private static string EffectText(string effect) => effect switch
    {
        "new_units_bonus_xp_15" => "New units are trained with bonus experience",
        "city_defense_plus_5"   => "+5 city defense strength",
        _                       => effect,
    };

    private static readonly HashSet<ResourceType> StrategicResources = new() { ResourceType.Horses, ResourceType.Iron };
    private static readonly HashSet<ResourceType> BonusResources = new()
    {
        ResourceType.Wheat, ResourceType.Fish, ResourceType.Cattle, ResourceType.Sheep,
        ResourceType.Deer, ResourceType.Stone, ResourceType.Banana,
    };

    private static string ResourceClass(ResourceType r)
        => StrategicResources.Contains(r) ? "Strategic"
         : BonusResources.Contains(r)     ? "Bonus"
         : "Luxury";

    // Prose-key suffix for an enum value (lower-cased name): "goldore", "jungle".
    private static string Key(Enum e) => e.ToString().ToLowerInvariant();

    // "goldore"/"gold_ore" -> "GoldOre" so Spaced() can split it into "Gold Ore".
    private static string Pascal(string raw)
    {
        var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var p in parts) sb.Append(char.ToUpperInvariant(p[0])).Append(p.Substring(1));
        return sb.Length == 0 ? raw : sb.ToString();
    }

    // Inserts spaces before interior capitals: "GoldOre" -> "Gold Ore", "Wetlands" -> "Wetlands".
    private static string Spaced(string pascal)
    {
        var sb = new StringBuilder();
        foreach (var ch in pascal)
        {
            if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' ');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
