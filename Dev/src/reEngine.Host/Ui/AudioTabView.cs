// @module: reEngine.Host.Ui.AudioTabView
// @purpose: Renders the F1 drawer Audio tab (device picker, scaled master stepper, mixer gains, DSP presets, FF mode, Sound Test) on PixelOverlayCanvas.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Globalization, Foundation.Core, reEngine.Host.Audio, reEngine.Host.Contracts
// @exports: AudioTabView

using System.Drawing;
using System.Globalization;
using Foundation.Core;
using reEngine.Host.Audio;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Immediate-mode renderer and pointer click dispatcher for the <c>F1</c> drawer <c>Audio</c> tab.
/// </summary>
public sealed class AudioTabView
{
    private readonly List<DrawerControl> _controls = new();
    private readonly List<HostWidgetBounds> _widgets = new();
    private int _scrollOffset;

    /// <summary>Gets the bottom Y coordinate occupied by rendered Audio tab content.</summary>
    public int ContentBottomY { get; private set; }

    /// <summary>Renders the Audio mixer, DSP filter, and Sound Test audition controls into <paramref name="body"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view, HostDrawerDescriptor descriptor) =>
        Render(canvas, body, view, descriptor, ref _scrollOffset, out _);

    internal void Render(
        PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view, HostDrawerDescriptor descriptor,
        ref int scrollOffset, out int maxScroll)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(descriptor);

        _controls.Clear();
        _widgets.Clear();

        HostAudioMixerSettings s = view.AudioSettings;
        HostSoundTestService jukebox = view.SoundTestService;
        var layout = new HostDrawerScrollLayout(body, view.UiTextScale, view.FocusedControlId);

        string devName = string.IsNullOrWhiteSpace(s.AudioOutputDevice) ? "System Default" : s.AudioOutputDevice!;
        layout.AddPill("Audio_Device", $"Output: {devName}", !string.IsNullOrWhiteSpace(s.AudioOutputDevice),
            HostOverlayPalette.AccentCyan, isRight => CycleDevice(view, isRight ? -1 : 1), _controls);

        string masterPct = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(s.MasterVolume * 100):F0}%");
        layout.AddStepper("Audio_MasterVolume", "Audio_MasterVolume_Dec", "Audio_MasterVolume_Inc",
            "Master Volume", masterPct, HostOverlayPalette.AccentCyan,
            isRight => UpdateAudio(view, view.AudioSettings with { MasterVolume = StepRatioWrap(view.AudioSettings.MasterVolume, isRight) }),
            _ => UpdateAudio(view, view.AudioSettings with { MasterVolume = ClampStep(view.AudioSettings.MasterVolume, -0.10) }),
            _ => UpdateAudio(view, view.AudioSettings with { MasterVolume = ClampStep(view.AudioSettings.MasterVolume, 0.10) }),
            _controls);

        layout.AddColumns(_controls,
            ("Audio_Mute", s.IsAudioMuted ? "Mute: ON" : "Mute: OFF", s.IsAudioMuted, false, HostOverlayPalette.AccentWarning,
                _ => UpdateAudio(view, view.AudioSettings with { IsAudioMuted = !view.AudioSettings.IsAudioMuted }), null),
            ("Audio_FocusMute", "Focus Mute", s.ShouldMuteOnFocusLoss, true, HostOverlayPalette.AccentCyan,
                _ => UpdateAudio(view, view.AudioSettings with { ShouldMuteOnFocusLoss = !view.AudioSettings.ShouldMuteOnFocusLoss }), null));

        string bgmLabel = string.Create(CultureInfo.InvariantCulture, $"BGM:{Math.Round(s.BgmVolume * 100):F0}%");
        string sfxLabel = string.Create(CultureInfo.InvariantCulture, $"SFX:{Math.Round(s.SfxVolume * 100):F0}%");
        string voxLabel = string.Create(CultureInfo.InvariantCulture, $"Vox:{Math.Round(s.VoiceVolume * 100):F0}%");
        layout.AddColumns(_controls,
            ("Audio_BgmGain", bgmLabel, true, false, HostOverlayPalette.AccentCyan,
                isRight => UpdateAudio(view, view.AudioSettings with { BgmVolume = StepRatioWrap(view.AudioSettings.BgmVolume, isRight) }), null),
            ("Audio_SfxGain", sfxLabel, true, false, HostOverlayPalette.AccentCyan,
                isRight => UpdateAudio(view, view.AudioSettings with { SfxVolume = StepRatioWrap(view.AudioSettings.SfxVolume, isRight) }), null),
            ("Audio_VoiceGain", voxLabel, true, false, HostOverlayPalette.AccentCyan,
                isRight => UpdateAudio(view, view.AudioSettings with { VoiceVolume = StepRatioWrap(view.AudioSettings.VoiceVolume, isRight) }), null));

        HostAudioFilterPreset preset = HostAudioFilterPresetCodec.DetectPreset(s.AudioLowPassHz, s.AudioHighPassHz);
        string lpText = s.AudioLowPassHz > 0 ? $"{s.AudioLowPassHz}Hz" : "Off";
        string hpText = s.AudioHighPassHz > 0 ? $"{s.AudioHighPassHz}Hz" : "Off";
        layout.AddPill("Audio_FilterPreset", $"Filter: {HostAudioFilterPresetCodec.ToDisplayLabel(preset)}",
            preset != HostAudioFilterPreset.Off, HostOverlayPalette.AccentCyan, _ =>
            {
                HostAudioFilterPreset cur = HostAudioFilterPresetCodec.DetectPreset(view.AudioSettings.AudioLowPassHz, view.AudioSettings.AudioHighPassHz);
                HostAudioFilterPreset next = HostAudioFilterPresetCodec.CycleNext(cur);
                (int lp, int hp) = HostAudioFilterPresetCodec.ToCutoffsHz(next);
                UpdateAudio(view, view.AudioSettings with { AudioLowPassHz = lp, AudioHighPassHz = hp });
            }, _controls);
        layout.AddText($"LP: {lpText} | HP: {hpText}", HostOverlayPalette.TextMuted);

        string widthLabel = string.Create(CultureInfo.InvariantCulture, $"Width: {s.AudioStereoWidthPercent:F0}%");
        string resampLabel = $"Resample: {HostAudioResamplingModeCodec.ToDisplayLabel(s.ResamplingMode)}";
        layout.AddColumns(_controls,
            ("Audio_StereoWidth", widthLabel, Math.Abs(s.AudioStereoWidthPercent - 100.0) > 0.1, false, HostOverlayPalette.AccentCyan,
                isRight => UpdateAudio(view, view.AudioSettings with { AudioStereoWidthPercent = StepStereoWidth(view.AudioSettings.AudioStereoWidthPercent, isRight) }), null),
            ("Audio_Resampling", resampLabel, s.ResamplingMode == HostAudioResamplingMode.SincCubic, false, HostOverlayPalette.AccentCyan, _ =>
            {
                HostAudioResamplingMode next = view.AudioSettings.ResamplingMode == HostAudioResamplingMode.SincCubic
                    ? HostAudioResamplingMode.Linear
                    : HostAudioResamplingMode.SincCubic;
                UpdateAudio(view, view.AudioSettings with { ResamplingMode = next });
            }, null));

        layout.AddPill("Audio_FfMode", $"FF Audio: {HostFastForwardAudioModeCodec.ToDisplayLabel(s.FastForwardAudioMode)}",
            s.FastForwardAudioMode != HostFastForwardAudioMode.Normal, HostOverlayPalette.AccentCyan,
            _ => UpdateAudio(view, view.AudioSettings with { FastForwardAudioMode = HostFastForwardAudioModeCodec.CycleNext(view.AudioSettings.FastForwardAudioMode) }),
            _controls);

        layout.AddText("SOUND TEST JUKEBOX", HostOverlayPalette.AccentCyan);
        string trackStatus = $"{jukebox.CurrentTrack.Title} [{(jukebox.IsPlaying ? "PLAY" : "STOP")}]";
        layout.AddText(trackStatus, jukebox.IsPlaying ? HostOverlayPalette.AccentSuccess : HostOverlayPalette.TextPrimary);
        layout.AddColumns(_controls,
            ("Audio_SoundTest_Prev", "< Prev", false, false, HostOverlayPalette.AccentCyan, _ =>
            {
                HostSoundTestTrack t = view.SoundTestService.PrevTrack();
                view.EventCallbacks?.OnSoundTestActionRequested("prev", t.TrackId);
            }, null),
            ("Audio_SoundTest_PlayStop", jukebox.IsPlaying ? "Stop" : "Play", jukebox.IsPlaying, false, HostOverlayPalette.AccentSuccess, _ =>
            {
                view.SoundTestService.TogglePlayStop();
                view.EventCallbacks?.OnSoundTestActionRequested(view.SoundTestService.IsPlaying ? "play" : "stop", view.SoundTestService.CurrentTrack.TrackId);
            }, null),
            ("Audio_SoundTest_Next", "Next >", false, false, HostOverlayPalette.AccentCyan, _ =>
            {
                HostSoundTestTrack t = view.SoundTestService.NextTrack();
                view.EventCallbacks?.OnSoundTestActionRequested("next", t.TrackId);
            }, null));

        HostDrawerScrollLayout.AppendExtraRows(layout, HostDrawerTabId.Audio, descriptor.ExtraRows, view, _controls);
        ContentBottomY = layout.Render(canvas, "AUDIO DSP & SOUND TEST", "Audio", ref scrollOffset, out maxScroll, _controls, d => view.ScrollActiveTab(d));
        foreach (DrawerControl c in _controls) _widgets.Add(new(c.ControlId, c.ControlType, c.Label, c.Bounds, true));
    }

    /// <summary>Returns interactive widget bounds emitted by the latest render pass.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    /// <summary>Processes a pointer click on the Audio tab.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, bool isRightClick = false)
    {
        foreach (DrawerControl c in _controls)
        {
            bool match = !string.IsNullOrEmpty(targetId)
                ? string.Equals(targetId, c.ControlId, StringComparison.OrdinalIgnoreCase)
                : c.Bounds.Contains(x, y);
            if (!match) continue;
            c.OnClick(isRightClick);
            return true;
        }
        return false;
    }

    private static void UpdateAudio(F1SettingsDrawerView view, HostAudioMixerSettings next)
    {
        view.AudioSettings = next;
        view.EventCallbacks?.OnAudioSettingsChanged(next);
    }

    private static void CycleDevice(F1SettingsDrawerView view, int step)
    {
        IReadOnlyList<HostAudioDeviceInfo> devices = view.AudioOutputDevices;
        int total = devices.Count + 1;
        int currentIdx = 0;
        if (!string.IsNullOrWhiteSpace(view.AudioSettings.AudioOutputDevice))
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].Name, view.AudioSettings.AudioOutputDevice, StringComparison.OrdinalIgnoreCase))
                {
                    currentIdx = i + 1;
                    break;
                }
            }
        }
        int nextIdx = (currentIdx + step + total) % total;
        string? nextDevice = nextIdx == 0 ? null : devices[nextIdx - 1].Name;
        HostAudioMixerSettings updated = view.AudioSettings with { AudioOutputDevice = nextDevice };
        view.AudioSettings = updated;
        view.EventCallbacks?.OnAudioDeviceChanged(nextDevice);
        view.EventCallbacks?.OnAudioSettingsChanged(updated);
    }

    private static double ClampStep(double current, double delta) =>
        Math.Clamp(Math.Round(current + delta, 2), 0.0, 1.0);

    private static double StepRatioWrap(double current, bool decrease)
    {
        double clamped = Math.Clamp(current, 0.0, 1.0);
        if (decrease) return clamped <= 0.01 ? 1.0 : Math.Max(0.0, Math.Round(clamped - 0.10, 2));
        return clamped >= 0.99 ? 0.0 : Math.Min(1.0, Math.Round(clamped + 0.10, 2));
    }

    private static double StepStereoWidth(double current, bool decrease)
    {
        double clamped = Math.Clamp(current, 0.0, 200.0);
        if (decrease) return clamped <= 0.5 ? 200.0 : Math.Max(0.0, Math.Round(clamped - 25.0, 0));
        return clamped >= 199.5 ? 0.0 : Math.Min(200.0, Math.Round(clamped + 25.0, 0));
    }
}
