// @module: reEngine.Host.Ui.AudioTabView
// @purpose: Renders the 6th F1 drawer Audio tab (device picker, mixer gains, focus mute, filter presets, stereo width, resampling, FF mode, Sound Test) on PixelOverlayCanvas.
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
    private sealed record ClickTarget(string ControlId, string ControlType, string Label, Rectangle Bounds, Action<bool> OnClick);

    private readonly List<ClickTarget> _targets = new();
    private readonly List<HostWidgetBounds> _widgets = new();

    /// <summary>Gets the bottom Y coordinate occupied by rendered Audio tab content.</summary>
    public int ContentBottomY { get; private set; }

    /// <summary>Renders the Audio mixer, DSP filter, and Sound Test audition controls into <paramref name="body"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view, HostDrawerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(descriptor);

        _targets.Clear();
        _widgets.Clear();

        HostAudioMixerSettings s = view.AudioSettings;
        HostSoundTestService jukebox = view.SoundTestService;
        const int gap = 4;
        const int rowH = 22;
        int colW2 = Math.Max(80, (body.Width - gap) / 2);
        int colW3 = Math.Max(60, (body.Width - (gap * 2)) / 3);
        int y = body.Y;

        canvas.DrawText(body.X, y, "AUDIO DSP & SOUND TEST", HostOverlayPalette.AccentCyan, 1);
        y += 14;

        string devName = string.IsNullOrWhiteSpace(s.AudioOutputDevice) ? "System Default" : s.AudioOutputDevice!;
        string devLabel = $"Output: {devName}";
        AddPill(canvas, new(body.X, y, body.Width, rowH), "Audio_Device", devLabel, !string.IsNullOrWhiteSpace(s.AudioOutputDevice), HostOverlayPalette.AccentCyan,
            isRight => CycleDevice(view, isRight ? -1 : 1));
        y += 26;

        string masterLabel = string.Create(CultureInfo.InvariantCulture, $"Master: {Math.Round(s.MasterVolume * 100):F0}%");
        AddPill(canvas, new(body.X, y, colW2, rowH), "Audio_MasterVolume", masterLabel, !s.IsAudioMuted, HostOverlayPalette.AccentCyan,
            isRight => UpdateAudio(view, view.AudioSettings with { MasterVolume = StepRatio(view.AudioSettings.MasterVolume, isRight) }));

        string muteLabel = s.IsAudioMuted ? "Mute: ON" : "Mute: OFF";
        AddPill(canvas, new(body.X + colW2 + gap, y, body.Width - colW2 - gap, rowH), "Audio_Mute", muteLabel, s.IsAudioMuted, HostOverlayPalette.AccentWarning,
            _ => UpdateAudio(view, view.AudioSettings with { IsAudioMuted = !view.AudioSettings.IsAudioMuted }));
        y += 26;

        AddToggle(canvas, new(body.X, y, body.Width, rowH), "Audio_FocusMute", "Mute on Focus Loss", s.ShouldMuteOnFocusLoss, HostOverlayPalette.AccentCyan,
            _ => UpdateAudio(view, view.AudioSettings with { ShouldMuteOnFocusLoss = !view.AudioSettings.ShouldMuteOnFocusLoss }));
        y += 26;

        string bgmLabel = string.Create(CultureInfo.InvariantCulture, $"BGM:{Math.Round(s.BgmVolume * 100):F0}%");
        string sfxLabel = string.Create(CultureInfo.InvariantCulture, $"SFX:{Math.Round(s.SfxVolume * 100):F0}%");
        string voxLabel = string.Create(CultureInfo.InvariantCulture, $"Vox:{Math.Round(s.VoiceVolume * 100):F0}%");
        AddPill(canvas, new(body.X, y, colW3, rowH), "Audio_BgmGain", bgmLabel, true, HostOverlayPalette.AccentCyan,
            isRight => UpdateAudio(view, view.AudioSettings with { BgmVolume = StepRatio(view.AudioSettings.BgmVolume, isRight) }));
        AddPill(canvas, new(body.X + colW3 + gap, y, colW3, rowH), "Audio_SfxGain", sfxLabel, true, HostOverlayPalette.AccentCyan,
            isRight => UpdateAudio(view, view.AudioSettings with { SfxVolume = StepRatio(view.AudioSettings.SfxVolume, isRight) }));
        AddPill(canvas, new(body.X + ((colW3 + gap) * 2), y, body.Width - ((colW3 + gap) * 2), rowH), "Audio_VoiceGain", voxLabel, true, HostOverlayPalette.AccentCyan,
            isRight => UpdateAudio(view, view.AudioSettings with { VoiceVolume = StepRatio(view.AudioSettings.VoiceVolume, isRight) }));
        y += 26;

        HostAudioFilterPreset preset = HostAudioFilterPresetCodec.DetectPreset(s.AudioLowPassHz, s.AudioHighPassHz);
        string filterLabel = $"Filter: {HostAudioFilterPresetCodec.ToDisplayLabel(preset)}";
        AddPill(canvas, new(body.X, y, body.Width, rowH), "Audio_FilterPreset", filterLabel, preset != HostAudioFilterPreset.Off, HostOverlayPalette.AccentCyan,
            _ =>
            {
                HostAudioFilterPreset cur = HostAudioFilterPresetCodec.DetectPreset(view.AudioSettings.AudioLowPassHz, view.AudioSettings.AudioHighPassHz);
                HostAudioFilterPreset next = HostAudioFilterPresetCodec.CycleNext(cur);
                (int lp, int hp) = HostAudioFilterPresetCodec.ToCutoffsHz(next);
                UpdateAudio(view, view.AudioSettings with { AudioLowPassHz = lp, AudioHighPassHz = hp });
            });
        y += 24;

        string lpText = s.AudioLowPassHz > 0 ? $"{s.AudioLowPassHz}Hz" : "Off";
        string hpText = s.AudioHighPassHz > 0 ? $"{s.AudioHighPassHz}Hz" : "Off";
        canvas.DrawText(body.X, y, HostOverlayLayoutHelpers.FitText($"LP: {lpText} | HP: {hpText}", body.Width, 1), HostOverlayPalette.TextMuted, 1);
        y += 14;

        string widthLabel = string.Create(CultureInfo.InvariantCulture, $"Width: {s.AudioStereoWidthPercent:F0}%");
        AddPill(canvas, new(body.X, y, colW2, rowH), "Audio_StereoWidth", widthLabel, Math.Abs(s.AudioStereoWidthPercent - 100.0) > 0.1, HostOverlayPalette.AccentCyan,
            isRight => UpdateAudio(view, view.AudioSettings with { AudioStereoWidthPercent = StepStereoWidth(view.AudioSettings.AudioStereoWidthPercent, isRight) }));

        string resampLabel = $"Resample: {HostAudioResamplingModeCodec.ToDisplayLabel(s.ResamplingMode)}";
        AddPill(canvas, new(body.X + colW2 + gap, y, body.Width - colW2 - gap, rowH), "Audio_Resampling", resampLabel, s.ResamplingMode == HostAudioResamplingMode.SincCubic, HostOverlayPalette.AccentCyan,
            _ =>
            {
                HostAudioResamplingMode next = view.AudioSettings.ResamplingMode == HostAudioResamplingMode.SincCubic
                    ? HostAudioResamplingMode.Linear
                    : HostAudioResamplingMode.SincCubic;
                UpdateAudio(view, view.AudioSettings with { ResamplingMode = next });
            });
        y += 26;

        string ffLabel = $"FF Audio: {HostFastForwardAudioModeCodec.ToDisplayLabel(s.FastForwardAudioMode)}";
        AddPill(canvas, new(body.X, y, body.Width, rowH), "Audio_FfMode", ffLabel, s.FastForwardAudioMode != HostFastForwardAudioMode.Normal, HostOverlayPalette.AccentCyan,
            _ => UpdateAudio(view, view.AudioSettings with { FastForwardAudioMode = HostFastForwardAudioModeCodec.CycleNext(view.AudioSettings.FastForwardAudioMode) }));
        y += 30;

        canvas.DrawText(body.X, y, "SOUND TEST JUKEBOX", HostOverlayPalette.AccentCyan, 1);
        y += 14;
        string trackStatus = $"{jukebox.CurrentTrack.Title} [{(jukebox.IsPlaying ? "PLAY" : "STOP")}]";
        canvas.DrawText(body.X, y, HostOverlayLayoutHelpers.FitText(trackStatus, body.Width, 1), jukebox.IsPlaying ? HostOverlayPalette.AccentSuccess : HostOverlayPalette.TextPrimary, 1);
        y += 14;

        AddPill(canvas, new(body.X, y, colW3, rowH), "Audio_SoundTest_Prev", "< Prev", false, HostOverlayPalette.AccentCyan,
            _ => { HostSoundTestTrack t = view.SoundTestService.PrevTrack(); view.EventCallbacks?.OnSoundTestActionRequested("prev", t.TrackId); });
        AddPill(canvas, new(body.X + colW3 + gap, y, colW3, rowH), "Audio_SoundTest_PlayStop", jukebox.IsPlaying ? "Stop" : "Play", jukebox.IsPlaying, HostOverlayPalette.AccentSuccess,
            _ =>
            {
                view.SoundTestService.TogglePlayStop();
                view.EventCallbacks?.OnSoundTestActionRequested(view.SoundTestService.IsPlaying ? "play" : "stop", view.SoundTestService.CurrentTrack.TrackId);
            });
        AddPill(canvas, new(body.X + ((colW3 + gap) * 2), y, body.Width - ((colW3 + gap) * 2), rowH), "Audio_SoundTest_Next", "Next >", false, HostOverlayPalette.AccentCyan,
            _ => { HostSoundTestTrack t = view.SoundTestService.NextTrack(); view.EventCallbacks?.OnSoundTestActionRequested("next", t.TrackId); });

        ContentBottomY = y + rowH + 6;
    }

    /// <summary>Returns interactive widget bounds emitted by the latest render pass.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    /// <summary>Processes a pointer click on the Audio tab.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, bool isRightClick = false)
    {
        foreach (ClickTarget target in _targets)
        {
            bool match = !string.IsNullOrEmpty(targetId)
                ? string.Equals(targetId, target.ControlId, StringComparison.OrdinalIgnoreCase)
                : target.Bounds.Contains(x, y);
            if (!match) continue;
            target.OnClick(isRightClick);
            return true;
        }
        return false;
    }

    private void AddPill(PixelOverlayCanvas canvas, Rectangle rect, string id, string label, bool active, RgbaColor accent, Action<bool> onClick)
    {
        HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, label, active, accent);
        _targets.Add(new(id, "Button", label, rect, onClick));
        _widgets.Add(new(id, "Button", label, rect, true));
    }

    private void AddToggle(PixelOverlayCanvas canvas, Rectangle rect, string id, string label, bool val, RgbaColor accent, Action<bool> onClick)
    {
        HostOverlayLayoutHelpers.DrawToggleRow(canvas, rect, label, val, accent);
        _targets.Add(new(id, "CheckBox", label, rect, onClick));
        _widgets.Add(new(id, "CheckBox", label, rect, true));
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

    private static double StepRatio(double current, bool decrease)
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
