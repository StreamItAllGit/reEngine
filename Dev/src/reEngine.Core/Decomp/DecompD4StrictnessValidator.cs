// @module: reEngine.Core.Decomp.DecompD4StrictnessValidator
// @purpose: Enforces Corpus\tiers layout, d4_build_report.json zero-warnings/tells, -Wno-everything ban, and zero C-body clean-room rules.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Text.Json, System.Text.RegularExpressions
// @exports: D4StrictnessValidationResult, DecompD4StrictnessValidator

using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace reEngine.Core.Decomp;

/// <summary>
/// Aggregate D4 craftsmanship &amp; clean-room strictness validation outcome.
/// </summary>
public sealed record D4StrictnessValidationResult(
    bool IsValid,
    IReadOnlyList<string> Violations,
    IReadOnlyList<string> DiscoveredTiers,
    string Summary);

/// <summary>
/// Audits D4/D5 build reports, toolchain flag files, <c>Corpus\tiers\</c>, and <c>d4_rewrites</c> / <c>.decomp</c> payloads.
/// </summary>
public static partial class DecompD4StrictnessValidator
{
    /// <summary>Canonical preserved decompilation tier directory names under <c>Corpus\tiers\</c>.</summary>
    public static readonly string[] CanonicalCorpusTiers =
    [
        "D1_Raw", "D2_Structured", "D3_Mapped", "D4_FunctionalRewrite", "D5_ByteExact"
    ];

    private static readonly string[] BannedSuppressionFlags =
    [
        "-Wno-everything", "-Wno-all"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [GeneratedRegex(@"(?:^|\s)-w(?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex BareWarnSuppressRegex();

    [GeneratedRegex(@"\b(?:void|int|uint|short|ushort|char|long|byte|uint8_t|uint16_t|uint32_t|int32_t)\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^;{]*\)\s*\{", RegexOptions.CultureInvariant)]
    private static partial Regex CFunctionBodyRegex();

    /// <summary>Validates a <c>d4_build_report.json</c> payload for zero warnings, zero decompiler tells, and allowed flags.</summary>
    public static D4StrictnessValidationResult ValidateD4BuildReportJson(string reportJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportJson);
        D4BuildReport? report = JsonSerializer.Deserialize<D4BuildReport>(reportJson, JsonOptions);
        if (report is null)
        {
            return new D4StrictnessValidationResult(false, ["d4_build_report.json deserialized to null."], Array.Empty<string>(), "Invalid D4 build report.");
        }

        var violations = new List<string>();
        if (report.CompilerErrorCount != 0) violations.Add($"compilerErrorCount={report.CompilerErrorCount} (expected 0).");
        if (report.CompilerWarningCount != 0) violations.Add($"compilerWarningCount={report.CompilerWarningCount} (expected 0).");
        if (report.CraftsmanshipTellCount != 0) violations.Add($"craftsmanshipTellCount={report.CraftsmanshipTellCount} (expected 0).");
        if (!report.IsZeroWarningsVerified) violations.Add("isZeroWarningsVerified is false.");
        if (!report.IsZeroDecompilerTellsVerified) violations.Add("isZeroDecompilerTellsVerified is false.");

        CheckFlagsText(string.Join(' ', report.CompilerFlags), "d4_build_report.json", violations);
        return BuildResult(violations, Array.Empty<string>());
    }

    /// <summary>Audits a workspace or decomp output root for clean <c>Corpus\tiers\</c>, config flags, and zero C bodies in <c>d4_rewrites</c>.</summary>
    public static D4StrictnessValidationResult ValidateWorkspace(string projectRootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootDir);
        string root = Path.GetFullPath(projectRootDir);
        var violations = new List<string>();
        var tiers = DiscoverCorpusTiers(root);

        foreach (string configName in new[] { ".clangd", "compile_commands.json" })
        {
            string path = Path.Combine(root, configName);
            if (File.Exists(path)) CheckFlagsText(File.ReadAllText(path), configName, violations);
        }

        string d4ReportPath = Path.Combine(root, "d4_build_report.json");
        if (File.Exists(d4ReportPath))
        {
            D4StrictnessValidationResult reportRes = ValidateD4BuildReportJson(File.ReadAllText(d4ReportPath));
            violations.AddRange(reportRes.Violations);
        }

        string rewritesDir = Path.Combine(root, "Dev", "schemas", "d4_rewrites");
        if (Directory.Exists(rewritesDir))
        {
            foreach (string jsonFile in Directory.EnumerateFiles(rewritesDir, "*.json", SearchOption.AllDirectories))
            {
                string content = File.ReadAllText(jsonFile);
                if (ContainsCFunctionBody(content))
                {
                    violations.Add($"Clean-room D4 violation: C function body detected inside '{Path.GetRelativePath(root, jsonFile)}'.");
                }
            }
        }

        return BuildResult(violations, tiers);
    }

    /// <summary>Audits a <c>.decomp</c> ZIP archive to reject embedded C source function bodies.</summary>
    public static D4StrictnessValidationResult ValidateDecompArchiveCleanRoom(string decompZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decompZipPath);
        var violations = new List<string>();
        using ZipArchive archive = ZipFile.OpenRead(decompZipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string ext = Path.GetExtension(entry.Name);
            if (ext.Equals(".c", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".cpp", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(entry.Open());
                string content = reader.ReadToEnd();
                if (ContainsCFunctionBody(content))
                {
                    violations.Add($"Archive entry '{entry.FullName}' contains forbidden C function body.");
                }
            }
        }
        return BuildResult(violations, Array.Empty<string>());
    }

    /// <summary>Discovers preserved tier folders inside <c>Corpus\tiers\</c>.</summary>
    public static IReadOnlyList<string> DiscoverCorpusTiers(string rootDir)
    {
        string tiersRoot = Path.Combine(rootDir, "Corpus", "tiers");
        if (!Directory.Exists(tiersRoot)) return Array.Empty<string>();
        return CanonicalCorpusTiers.Where(t => Directory.Exists(Path.Combine(tiersRoot, t))).ToList();
    }

    /// <summary>Detects embedded C function bodies or <c>cBody</c>/<c>functionBody</c> schema payloads.</summary>
    public static bool ContainsCFunctionBody(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("\"cBody\"", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("\"functionBody\"", StringComparison.OrdinalIgnoreCase) ||
               CFunctionBodyRegex().IsMatch(text);
    }

    private static void CheckFlagsText(string text, string sourceLabel, List<string> violations)
    {
        foreach (string banned in BannedSuppressionFlags)
        {
            if (text.Contains(banned, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"Banned warning-suppression flag '{banned}' detected in '{sourceLabel}'.");
            }
        }
        if (BareWarnSuppressRegex().IsMatch(text))
        {
            violations.Add($"Banned bare '-w' warning-suppression flag detected in '{sourceLabel}'.");
        }
    }

    private static D4StrictnessValidationResult BuildResult(List<string> violations, IReadOnlyList<string> tiers) =>
        violations.Count == 0
            ? new D4StrictnessValidationResult(true, Array.Empty<string>(), tiers, "D4 strictness & clean-room checks passed (0 warnings, 0 tells).")
            : new D4StrictnessValidationResult(false, violations, tiers, $"D4 strictness check failed ({violations.Count} violation(s)): {string.Join("; ", violations)}");
}
