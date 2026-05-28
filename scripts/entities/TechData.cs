using System.Collections.Generic;

namespace NWO.Entities;

// Immutable tech definition loaded from data/techs.json. Never modified at runtime.
// Unlocks.Buildings / Unlocks.Units are wired in Phase 5; Improvements and
// RevealedResources are parsed but unused until later phases add those systems.
public record TechData
{
    public string       Id            { get; init; } = "";
    public string       Name          { get; init; } = "";
    public int          ScienceCost   { get; init; }
    public List<string> Prerequisites { get; init; } = new();
    public TechUnlocks  Unlocks       { get; init; } = new();
}

public record TechUnlocks
{
    public List<string> Buildings          { get; init; } = new();
    public List<string> Units              { get; init; } = new();
    public List<string> Improvements       { get; init; } = new();
    public List<string> RevealedResources  { get; init; } = new();
}
