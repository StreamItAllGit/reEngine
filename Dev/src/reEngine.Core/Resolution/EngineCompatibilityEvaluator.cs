// @module: reEngine.Core.Resolution.EngineCompatibilityEvaluator
// @purpose: Evaluates Standard 18 incompatibleEngineVersions SemVer, wildcard (0.2.x, 0.*), and comparator range (>=0.5.0 <0.5.2) rules.
// @depends-on: System, System.Collections.Generic, System.Globalization
// @exports: EngineCompatibilityEvaluator

using System.Globalization;

namespace reEngine.Core.Resolution;

/// <summary>
/// Parses and evaluates <c>incompatibleEngineVersions</c> rules against candidate <see cref="SemVer"/> engine releases.
/// </summary>
public static class EngineCompatibilityEvaluator
{
    private static readonly string[] Operators = [">=", "<=", "!=", "==", ">", "<", "="];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="candidate"/> matches any rule in <paramref name="rules"/>.
    /// </summary>
    /// <param name="candidate">Installed engine version under evaluation.</param>
    /// <param name="rules">Optional list of SemVer, wildcard, or comparator range rules.</param>
    public static bool IsVersionIncompatible(SemVer candidate, IReadOnlyList<string>? rules)
    {
        if (rules is null || rules.Count == 0) return false;
        for (int i = 0; i < rules.Count; i++)
        {
            if (EvaluateRule(candidate, rules[i])) return true;
        }
        return false;
    }

    /// <summary>
    /// Evaluates a single incompatibility rule string (AND across space-separated comparator clauses).
    /// </summary>
    /// <param name="candidate">Installed engine version under evaluation.</param>
    /// <param name="rule">Single exact, wildcard, or range rule string (e.g. <c>"&gt;=0.5.0 &lt;0.5.2"</c>).</param>
    public static bool EvaluateRule(SemVer candidate, string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return false;
        List<string> clauses = TokenizeClauses(rule);
        if (clauses.Count == 0) return false;
        for (int i = 0; i < clauses.Count; i++)
        {
            if (!EvaluateClause(candidate, clauses[i])) return false;
        }
        return true;
    }

    private static List<string> TokenizeClauses(string rule)
    {
        string[] rawTokens = rule.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        var clauses = new List<string>(rawTokens.Length);
        for (int i = 0; i < rawTokens.Length; i++)
        {
            string token = rawTokens[i].Trim();
            if (token.Length == 0) continue;
            if (token is ">=" or "<=" or "!=" or "==" or ">" or "<" or "=" && i + 1 < rawTokens.Length)
                clauses.Add(token + rawTokens[++i].Trim());
            else
                clauses.Add(token);
        }
        return clauses;
    }

    private static bool EvaluateClause(SemVer candidate, string clause)
    {
        string op = string.Empty;
        string operand = clause;
        foreach (string prefix in Operators)
        {
            if (!clause.StartsWith(prefix, StringComparison.Ordinal)) continue;
            op = prefix;
            operand = clause[prefix.Length..].Trim();
            break;
        }

        if (operand.Length == 0) return false;
        if (op is "" or "=" or "==") return TryMatchPattern(candidate, operand, out bool m) && m;
        if (op == "!=") return TryMatchPattern(candidate, operand, out bool m) && !m;
        if (!TryParseBoundVersion(operand, out SemVer bound)) return false;

        int cmp = candidate.CompareTo(bound);
        return op switch
        {
            ">=" => cmp >= 0,
            "<=" => cmp <= 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            _ => false
        };
    }

    private static bool TryMatchPattern(SemVer candidate, string pattern, out bool matched)
    {
        matched = false;
        if (SemVer.TryParse(pattern, out SemVer exact)) { matched = candidate.CompareTo(exact) == 0; return true; }

        string norm = pattern.Trim();
        if (norm.StartsWith('v') || norm.StartsWith('V')) norm = norm[1..];
        if (IsWildcard(norm)) { matched = true; return true; }

        string[] parts = norm.Split('.');
        if (parts.Length is < 2 or > 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
            return false;

        if (parts.Length == 2)
        {
            if (IsWildcard(parts[1])) { matched = candidate.Major == major; return true; }
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int min2)) return false;
            matched = candidate.Major == major && candidate.Minor == min2;
            return true;
        }

        if (IsWildcard(parts[1]) && IsWildcard(parts[2])) { matched = candidate.Major == major; return true; }
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)) return false;
        if (IsWildcard(parts[2])) { matched = candidate.Major == major && candidate.Minor == minor; return true; }
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch)) return false;
        matched = candidate.Major == major && candidate.Minor == minor && candidate.Patch == patch;
        return true;
    }

    private static bool TryParseBoundVersion(string raw, out SemVer bound)
    {
        if (SemVer.TryParse(raw, out bound)) return true;
        string norm = raw.Trim();
        if (norm.StartsWith('v') || norm.StartsWith('V')) norm = norm[1..];
        string[] parts = norm.Split('.');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int maj) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int min))
        {
            bound = new SemVer(maj, min, 0);
            return true;
        }
        bound = default;
        return false;
    }

    private static bool IsWildcard(string token) =>
        string.Equals(token, "x", StringComparison.OrdinalIgnoreCase) || token == "*";
}
