// @module: reEngine.Core.Decomp.DecompPipelineContracts
// @purpose: D0-D5 decompilation pipeline stage result, run options, and 3-part unbuilt-engine error contract.
// @depends-on: System, System.Collections.Generic
// @exports: DecompStage, DecompStageResult, DecompUnbuiltEngineError, DecompRunOptions

namespace reEngine.Core.Decomp;

/// <summary>
/// Canonical clean-room decompilation pipeline milestones (<c>D0</c>–<c>D5</c>).
/// </summary>
public enum DecompStage
{
    /// <summary>D0: ROM/ISO checksum verification and media intake.</summary>
    D0 = 0,

    /// <summary>D1: Binary section discovery and entry-point mapping.</summary>
    D1 = 1,

    /// <summary>D2: Offline Ghidra headless symbol/type recovery.</summary>
    D2 = 2,

    /// <summary>D3: Clean-room C/IR lift recipe emission.</summary>
    D3 = 3,

    /// <summary>D4: Matching/equivalence verification against golden fixtures.</summary>
    D4 = 4,

    /// <summary>D5: Full shiftable clean-room decompilation package.</summary>
    D5 = 5
}

/// <summary>
/// Result returned by <c>IDecompPlatformProvider.RunDecompAsync</c> for a pipeline stage.
/// </summary>
public sealed record DecompStageResult(
    DecompStage Stage,
    bool Success,
    string Message,
    string? OutputDirectory,
    IReadOnlyList<string> GeneratedFiles,
    TimeSpan Elapsed,
    string? DiagnosticCode = null);

/// <summary>
/// Deterministic 3-part remediation error emitted when a <c>.decomp</c> or <c>.recomp</c> module targets
/// an unbuilt or missing platform engine (e.g., <c>re64</c>, <c>reGenesis</c>, <c>reCube</c>).
/// </summary>
public sealed record DecompUnbuiltEngineError(
    string EngineId,
    string CompatibleEngineTrack,
    string MinimumEngineVersion,
    string WhatFailed,
    string MissingRequirement,
    string RemediationAction)
{
    /// <summary>
    /// Creates a deterministic 3-part unbuilt-engine error payload.
    /// </summary>
    public static DecompUnbuiltEngineError Create(
        string engineId,
        string compatibleEngineTrack,
        string minimumEngineVersion,
        string? requestedModuleId = null)
    {
        string safeEngine = string.IsNullOrWhiteSpace(engineId) ? "<unknown-engine>" : engineId.Trim();
        string safeTrack = string.IsNullOrWhiteSpace(compatibleEngineTrack) ? "*" : compatibleEngineTrack.Trim();
        string safeMinVer = string.IsNullOrWhiteSpace(minimumEngineVersion) ? "0.1.0" : minimumEngineVersion.Trim();
        string moduleContext = string.IsNullOrWhiteSpace(requestedModuleId)
            ? "Cannot dispatch decompilation pipeline"
            : $"Cannot dispatch decompilation pipeline for module '{requestedModuleId.Trim()}'";

        return new DecompUnbuiltEngineError(
            safeEngine,
            safeTrack,
            safeMinVer,
            WhatFailed: $"{moduleContext}: required platform engine '{safeEngine}' is not installed or not yet built.",
            MissingRequirement: $"Missing engine '{safeEngine}' (compatibleEngineTrack '{safeTrack}', >= v{safeMinVer}).",
            RemediationAction: $"Install '{safeEngine}' >= v{safeMinVer} under your reEngine engines\\ folder, or pass --engine-dir <path>.");
    }

    /// <summary>
    /// Formats the 3-part deterministic remediation message for CLI and UI diagnostics.
    /// </summary>
    public string FormatMessage() =>
        $"[1/3 What Failed] {WhatFailed}{Environment.NewLine}" +
        $"[2/3 Missing] {MissingRequirement}{Environment.NewLine}" +
        $"[3/3 Remediation] {RemediationAction}";
}

/// <summary>
/// Execution options passed to <c>IDecompPlatformProvider.RunDecompAsync</c>.
/// </summary>
public sealed record DecompRunOptions(
    string PackagePath,
    string OutputDirectory,
    string DataDirectory,
    DecompStage TargetStage = DecompStage.D3,
    string? EngineDirectoryOverride = null,
    string? ToolchainDirectory = null,
    bool DryRun = false,
    bool Headless = true,
    IReadOnlyDictionary<string, string>? PlatformArguments = null);
