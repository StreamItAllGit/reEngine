// @module: reEngine.Core.Bootstrap.ReEnginePackagingConstants
// @purpose: Canonical branding, packaging, and containment constants plus bootstrap result record for reEngine.
// @depends-on: System
// @exports: ReEnginePackagingConstants, ReEngineBootstrapResult

namespace reEngine.Core.Bootstrap;

/// <summary>
/// Canonical branding, packaging, and containment constants for <c>reEngine</c>.
/// </summary>
public static class ReEnginePackagingConstants
{
    /// <summary>Canonical lowercase application identifier.</summary>
    public const string AppId = "reengine";

    /// <summary>Human-facing Title Case display name.</summary>
    public const string DisplayName = "reEngine";

    /// <summary>Current packaged SemVer version.</summary>
    public const string Version = "0.2.1";

    /// <summary>Canonical studio umbrella folder under <c>%LOCALAPPDATA%\Programs\</c>.</summary>
    public const string StudioName = "StreamItAll";

    /// <summary>Canonical suite folder nested under <see cref="StudioName"/>.</summary>
    public const string SuiteName = "reEngine";

    /// <summary>Canonical umbrella subpath relative to <c>%LOCALAPPDATA%\Programs\</c> (Standard 18: umbrellaSubpath).</summary>
    public const string UmbrellaSubpath = "StreamItAll\\reEngine\\reEngine";

    /// <summary>Default installer footprint profile identifier (Standard 18: installProfile).</summary>
    public const string DefaultInstallProfile = "lightweight";

    /// <summary>Brand accent color hex (Sky Cyan).</summary>
    public const string BrandAccentHex = "#38BDF8";

    /// <summary>Zero-daemon invariant: <c>reEngine</c> never auto-starts at Windows sign-in.</summary>
    public const bool AllowAutoStart = false;

    /// <summary>Default relative preload directory inside game module roots.</summary>
    public const string DefaultPreloadRomDir = "data/rom";

    /// <summary>Filename of the virtualized association store in <c>--test-mode</c>.</summary>
    public const string VirtualRegistryFileName = "virtual_registry.json";
}

/// <summary>
/// Outcome of bootstrapping or verifying a <c>reEngine</c> installation root.
/// </summary>
public sealed record ReEngineBootstrapResult(
    bool Success,
    string ReEngineInstallDir,
    string EnginesRootDir,
    bool InstalledOrUpgraded,
    bool AssociationRegistered,
    string DiagnosticMessage);
