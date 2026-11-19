// @module: reEngine.App.Program.Verify
// @purpose: Package target classifier and interactive .decomp UI decision helpers shared cleanly with tests.
// @depends-on: System, System.IO, Foundation.Core, reEngine.Core
// @exports: Program (partial: IsPackageVerifyTarget, ShouldOpenInteractiveDecompUi, FormatVerificationSummary)

using Foundation.Core;
using reEngine.Core.Decomp;

namespace reEngine.App;

internal static partial class Program
{
    internal static bool IsPackageVerifyTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        string trimmed = target.Trim();
        if (trimmed.IndexOfAny(['/', '\\']) >= 0 || trimmed.StartsWith('.')) return true;
        if (trimmed.EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".decomp", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return File.Exists(trimmed) || Directory.Exists(trimmed);
    }

    internal static bool ShouldOpenInteractiveDecompUi(
        RecompLaunchOptions launchOpts,
        AppEnvironment appEnv,
        bool isIsolatedTest,
        bool explicitWindow,
        bool isOutputRedirected) =>
        !launchOpts.Headless &&
        !appEnv.DryRun &&
        !launchOpts.TrayOnly &&
        ((!isOutputRedirected && !isIsolatedTest) || explicitWindow);

    internal static string FormatVerificationSummary(PackageVerificationReport report, string target)
    {
        ArgumentNullException.ThrowIfNull(report);
        string engineLabel = report.ResolvedEngine is not null
            ? $"{report.ResolvedEngine.EngineId} v{report.ResolvedEngine.Version}"
            : (report.EngineId ?? "unknown");
        return $"Verify {report.PackageKind} '{report.ModuleId ?? target}': passed={report.IsPassed}, exitCode={report.ExitCode}, engine={engineLabel}, mediaReady={report.HasMediaReady}";
    }
}
