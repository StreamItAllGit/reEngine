// @module: reEngine.Core.Resolution.EngineAliasCatalog
// @purpose: N-way SeriesMap coded/legacy hardware engineId equivalence catalog (reSlate<->reStation, reCart<->reCartOne<->reNes, reCart64<->re64, reBlast16<->reGenesis).
// @depends-on: System, System.Collections.Generic, System.Linq
// @exports: EngineAliasCatalog

namespace reEngine.Core.Resolution;

/// <summary>
/// Maps coded and legacy hardware engine identifiers cleanly across N-way <c>SeriesMap.json</c> equivalence groups.
/// </summary>
public static class EngineAliasCatalog
{
    private static readonly string[][] EquivalenceGroups =
    [
        ["reSlate", "reStation"],
        ["reCart", "reCartOne", "reNes"],
        ["reCart64", "re64"],
        ["reBlast16", "reGenesis"]
    ];

    /// <summary>
    /// Returns the requested <paramref name="engineId"/> first followed by all other identifiers in its equivalence group.
    /// </summary>
    public static IReadOnlyList<string> GetEquivalentIds(string? engineId)
    {
        if (string.IsNullOrWhiteSpace(engineId))
        {
            return Array.Empty<string>();
        }

        string trimmed = engineId.Trim();
        var result = new List<string>(4) { trimmed };

        foreach (string[] group in EquivalenceGroups)
        {
            if (!group.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string candidate in group)
            {
                if (!result.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Attempts to resolve the primary alias for <paramref name="engineId"/> within its equivalence group.
    /// </summary>
    public static bool TryGetAlias(string? engineId, out string alias)
    {
        IReadOnlyList<string> equivalents = GetEquivalentIds(engineId);
        if (equivalents.Count > 1)
        {
            alias = equivalents[1];
            return true;
        }
        alias = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns true if two engine identifiers refer to the same canonical or coded hardware engine.
    /// </summary>
    public static bool AreEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }
        foreach (string candidate in GetEquivalentIds(left))
        {
            if (string.Equals(candidate, right.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
