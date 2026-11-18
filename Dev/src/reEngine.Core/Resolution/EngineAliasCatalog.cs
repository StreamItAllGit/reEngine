// @module: reEngine.Core.Resolution.EngineAliasCatalog
// @purpose: Bidirectional SeriesMap coded/legacy hardware engineId alias catalog (reSlate<->reStation, reCartOne<->reNes, reCart64<->re64, reBlast16<->reGenesis).
// @depends-on: System, System.Collections.Generic
// @exports: EngineAliasCatalog

namespace reEngine.Core.Resolution;

/// <summary>
/// Maps coded hardware engine identifiers cleanly to/from legacy <c>re*</c> identifiers per <c>SeriesMap.json</c>.
/// </summary>
public static class EngineAliasCatalog
{
    private static readonly (string Coded, string Legacy)[] CanonicalPairs =
    [
        ("reSlate", "reStation"),
        ("reCartOne", "reNes"),
        ("reCart64", "re64"),
        ("reBlast16", "reGenesis")
    ];

    /// <summary>
    /// Returns the requested <paramref name="engineId"/> first followed by any bidirectional alias equivalents.
    /// </summary>
    public static IReadOnlyList<string> GetEquivalentIds(string? engineId)
    {
        if (string.IsNullOrWhiteSpace(engineId))
        {
            return Array.Empty<string>();
        }

        string trimmed = engineId.Trim();
        var result = new List<string>(2) { trimmed };

        foreach ((string coded, string legacy) in CanonicalPairs)
        {
            if (string.Equals(trimmed, coded, StringComparison.OrdinalIgnoreCase) &&
                !result.Contains(legacy, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(legacy);
            }
            else if (string.Equals(trimmed, legacy, StringComparison.OrdinalIgnoreCase) &&
                     !result.Contains(coded, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(coded);
            }
        }

        return result;
    }

    /// <summary>
    /// Attempts to resolve the single bidirectional alias for <paramref name="engineId"/>.
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
