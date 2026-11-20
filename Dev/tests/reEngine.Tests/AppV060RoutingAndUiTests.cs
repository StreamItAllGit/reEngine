// @module: reEngine.Tests.AppV060RoutingAndUiTests
// @purpose: xUnit tests for ValueOptions parity, v0.6.0 CLI parser/router gating, WinForms LibraryDisplayAudioPanel persistence, and DetailsRightDrawer.
// @depends-on: System, System.Collections.Generic, System.IO, System.Threading, reEngine.App, reEngine.Core, reEngine.Host, reEngine.Launcher, Xunit
// @exports: AppV060RoutingAndUiTests

using Foundation.Core;
using reEngine.App;
using reEngine.App.Ui;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;
using reEngine.Host.Settings;
using reEngine.Launcher;
using Xunit;

namespace reEngine.Tests;

public sealed class AppV060RoutingAndUiTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_t4_" + Guid.NewGuid().ToString("N"));

    public AppV060RoutingAndUiTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }

    [Fact]
    public void ValueOptions_StayIdenticalBetweenLauncherAndApp()
    {
        Assert.True(LauncherArgsHelper.ValueOptions.SetEquals(RecompLaunchOptions.ValueOptions));
        Assert.True(RecompLaunchOptions.ValueOptions.SetEquals(LauncherArgsHelper.ValueOptions));
    }

    [Fact]
    public void RecompLaunchOptionParser_ParsesV060FlagsAndRejectsInvalidValues()
    {
        var appEnv = new AppEnvironment("reengine", ["--data-dir", _tempRoot]);
        RecompLaunchOptions parsed = RecompLaunchOptionParser.Parse([
            "--no-preload-media-to-ram", "--fast-boot", "--safe-mode-reset",
            "--execution-tier", "aotWithThreadedIr",
            "--display-mode", "exclusiveFullscreen", "--monitor", "1",
            "--target-fps", "120", "--bg-fps-cap", "15", "--no-vsync",
            "--video-filter", "crt-aperture", "--no-true-color", "--dithering",
            "--hud-anchor", "screenEdges", "--screenshot-mode", "filteredComposite",
            "--ff-multiplier", "3.5", "--audio-device", "Headphones",
            "--master-volume", "0.7", "--audio-muted", "--mute-on-focus-loss",
            "--audio-lowpass-hz", "4800", "--audio-highpass-hz", "90",
            "--stereo-width", "140", "--audio-resampling", "linear", "--ff-audio-mode", "muted"
        ], appEnv, RecompLaunchOptions.ValueOptions);

        Assert.False(parsed.PreloadMediaToRam);
        Assert.True(parsed.FastBoot);
        Assert.True(parsed.SafeModeReset);
        Assert.Equal("aotWithThreadedIr", parsed.ExecutionTierMode);
        Assert.Equal(HostDisplayMode.ExclusiveFullscreen, parsed.DisplayMode);
        Assert.Equal(HostVideoFilterMode.CrtLite, parsed.VideoFilterMode);
        Assert.Equal(HostHudAnchorMode.ScreenEdges, parsed.HudAnchorMode);
        Assert.Equal(HostScreenshotCaptureMode.FilteredComposite, parsed.ScreenshotCaptureMode);
        Assert.Equal(3.5, parsed.FastForwardMultiplier);
        Assert.Equal(HostAudioResamplingMode.Linear, parsed.AudioResamplingMode);
        Assert.Equal(HostFastForwardAudioMode.Muted, parsed.FastForwardAudioMode);

        Assert.Throws<ArgumentException>(() => { _ = RecompLaunchOptionParser.Parse(["--ff-multiplier", "9.0"], appEnv, RecompLaunchOptions.ValueOptions); });
        Assert.Throws<ArgumentException>(() => { _ = RecompLaunchOptionParser.Parse(["--video-filter", "invalidFilter"], appEnv, RecompLaunchOptions.ValueOptions); });
    }

    [Fact]
    public void RecompLaunchFlagRouter_GatesV060FlagsByChildProtocolVersion()
    {
        var manifest = new RecompPackageManifest { ModuleId = "test-pkg", EngineId = "reStation", Title = "Test" };
        var v060Engine = new InstalledEngineCandidate("reStation", new SemVer(0, 6, 0), "v0.6.0", Path.Combine(_tempRoot, "e060"), Path.Combine(_tempRoot, "e060", "reStation.exe"));
        var v050Engine = new InstalledEngineCandidate("reStation", new SemVer(0, 5, 0), "v0.5.0", Path.Combine(_tempRoot, "e050"), Path.Combine(_tempRoot, "e050", "reStation.exe"));

        Assert.True(RecompLaunchFlagRouter.Supports060Routing(v060Engine));
        Assert.False(RecompLaunchFlagRouter.Supports060Routing(v050Engine));

        var opts = new RecompLaunchOptions
        {
            DisplayMode = HostDisplayMode.BorderlessFullscreen,
            VideoFilterMode = HostVideoFilterMode.Xbrz,
            PreloadMediaToRam = true,
            AudioResamplingMode = HostAudioResamplingMode.SincCubic,
            AspectRatioMode = HostAspectRatioMode.HorPlus21By9
        };

        var args060 = new List<string>();
        RecompLaunchFlagRouter.AppendRoutedFlags(args060, opts, manifest, v060Engine, bundle: null, log: null);
        Assert.Contains("--display-mode", args060);
        Assert.Contains("--video-filter", args060);
        Assert.Contains("xbrz", args060);
        Assert.Contains("--preload-media-to-ram", args060);
        Assert.Contains("--audio-resampling", args060);

        var warns = new List<string>();
        var args050 = new List<string>();
        RecompLaunchFlagRouter.AppendRoutedFlags(args050, opts, manifest, v050Engine, bundle: null, (lvl, msg) => warns.Add($"{lvl}:{msg}"));
        Assert.DoesNotContain("--display-mode", args050);
        Assert.DoesNotContain("--video-filter", args050);
        Assert.Contains("--aspect-mode", args050);
        Assert.Contains(warns, w => w.StartsWith("WARN:", StringComparison.Ordinal));
    }

    [Fact]
    public void WinFormsDisplayAudioPanelAndDrawer_RoundtripSplitSettingsAndResetSafeMode()
    {
        RunOnStaThread(() =>
        {
            string dataDir = Path.Combine(_tempRoot, "ui_data");
            var store = new HostSettingsStore(dataDir);
            ReEngineSettingsBundle bundle = store.LoadOrMigrate();
            bundle.Core.IsSafeModeActive = true;
            store.SaveCore(bundle.Core);

            var drawer = new DetailsRightDrawer(() => { }, () => { });
            var shelf = new LibraryShelfPanel(() => { }, (_, _, _) => { });
            LibraryDisplayAudioPanel da = shelf.DisplayAudioPanel;
            da.ApplyBundle(bundle);
            Assert.Equal("SAFE MODE: ACTIVE", da.SafeModeBadgeLabel.Text);
            Assert.True(da.ResetSafeModeButton.Enabled);

            da.PreloadMediaToRamCheckBox.Checked = true;
            da.FastBootCheckBox.Checked = true;
            da.DisplayModeCombo.SelectedItem = "borderlessFullscreen";
            da.VideoFilterCombo.SelectedItem = "crtLite";
            da.TrueColorCheckBox.Checked = false;
            da.DitheringCheckBox.Checked = true;
            da.AudioMutedCheckBox.Checked = true;
            da.AudioFilterPresetCombo.SelectedItem = "crtWarm";

            da.WriteToBundle(bundle, fullscreenChecked: true);
            store.SaveAll(bundle);

            ReEngineSettingsBundle reloaded = store.LoadOrMigrate();
            Assert.True(reloaded.Core.ShouldPreloadMediaToRam);
            Assert.True(reloaded.Core.IsFastBootEnabled);
            Assert.Equal("borderlessFullscreen", reloaded.Video.DisplayMode);
            Assert.Equal("crtLite", reloaded.Video.VideoFilterMode);
            Assert.False(reloaded.Video.IsTrueColor24BitEnabled);
            Assert.True(reloaded.Audio.IsAudioMuted);
            Assert.Equal(4800, reloaded.Audio.AudioLowPassHz);

            drawer.Populate(null, false, "vulkan", 2, new DecompDispatchStatusSummary(false, string.Empty, 0, "lightweight"), new AotCacheStatusSummary(string.Empty, 0, 0L), dataDir, "None", reloaded);
            Assert.Contains("CRT Lite", drawer.PresentationSummaryLabel.Text, StringComparison.Ordinal);
            Assert.Contains("CRT Warm", drawer.PresentationSummaryLabel.Text, StringComparison.Ordinal);
            Assert.Contains("Safe Mode: ACTIVE", drawer.TierBadgeLabel.Text, StringComparison.Ordinal);

            ReEngineSettingsBundle cleared = store.ClearSafeMode(reloaded);
            da.ApplyBundle(cleared);
            Assert.Equal("SAFE MODE: OFF", da.SafeModeBadgeLabel.Text);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
