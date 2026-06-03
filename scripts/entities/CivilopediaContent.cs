using System.Collections.Generic;

namespace NWO.Entities;

// Authored player-facing documentation loaded from data/civilopedia.json. Kept apart
// from the mechanical data records (UnitData/FactionData/…) so flavor text never has to
// touch the gameplay files. Two kinds of content:
//
//   • Prose    — key ("faction:dominion", "unit:warrior", "terrain:jungle") -> flavor
//                text, merged onto the live stats of a catalog/enum entry by
//                CivilopediaService. A missing key degrades to a stats-only entry.
//   • Articles — standalone entries (lore, rules) with no backing game entity, grouped
//                into a category ("world", "mechanics").
public record CivilopediaContent
{
    public Dictionary<string, string>   Prose    { get; init; } = new();
    public List<CivilopediaArticle>     Articles { get; init; } = new();

    // Shared empty content — the service renders stats-only entries against it.
    public static readonly CivilopediaContent Empty = new();
}

public record CivilopediaArticle
{
    public string Id       { get; init; } = "";
    public string Category { get; init; } = ""; // "world" | "mechanics"
    public string Title    { get; init; } = "";
    public string Body     { get; init; } = "";
}
