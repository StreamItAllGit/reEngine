// @module: reEngine.Tests.HostAudioDspAndJukeboxTests
// @purpose: xUnit tests for HostBiquadFilter LP/HP, HostAudioDspPipeline mix/width/resample/focus-ramp, HostSoundTestService, and 6th Audio drawer tab.
// @depends-on: System, reEngine.Host, Xunit
// @exports: HostAudioDspAndJukeboxTests

using Foundation.Core;
using reEngine.Host.Audio;
using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Ui;
using Xunit;

namespace reEngine.Tests;

public sealed class HostAudioDspAndJukeboxTests
{
    [Fact]
    public void HostBiquadFilter_AttenuatesStopBandsWithoutDenormals()
    {
        var lowPass = new HostBiquadFilter(HostBiquadKind.LowPass, cutoffHz: 400.0, sampleRate: 44100);
        short[] lowFreq = BuildSineStereo(44100, 120.0, frames: 2048);
        short[] highFreq = BuildSineStereo(44100, 8000.0, frames: 2048);

        lowPass.ProcessInterleaved(lowFreq);
        lowPass.Reset();
        lowPass.ProcessInterleaved(highFreq);

        double lpLowRms = ComputeRms(lowFreq.AsSpan(lowFreq.Length / 2));
        double lpHighRms = ComputeRms(highFreq.AsSpan(highFreq.Length / 2));
        Assert.True(lpLowRms > lpHighRms * 5.0, $"Expected 120Hz RMS ({lpLowRms:F1}) >> 8kHz RMS ({lpHighRms:F1}) under 400Hz LP.");

        var highPass = new HostBiquadFilter(HostBiquadKind.HighPass, cutoffHz: 3000.0, sampleRate: 44100);
        short[] lowForHp = BuildSineStereo(44100, 100.0, frames: 2048);
        short[] highForHp = BuildSineStereo(44100, 9000.0, frames: 2048);

        highPass.ProcessInterleaved(lowForHp);
        highPass.Reset();
        highPass.ProcessInterleaved(highForHp);

        double hpLowRms = ComputeRms(lowForHp.AsSpan(lowForHp.Length / 2));
        double hpHighRms = ComputeRms(highForHp.AsSpan(highForHp.Length / 2));
        Assert.True(hpHighRms > hpLowRms * 5.0, $"Expected 9kHz RMS ({hpHighRms:F1}) >> 100Hz RMS ({hpLowRms:F1}) under 3kHz HP.");
    }

    [Fact]
    public void HostAudioDspPipeline_AppliesMix_StereoWidth_Resampling_FfClamp_AndFocusRamp()
    {
        var dsp = new HostAudioDspPipeline(44100, 2, new HostAudioMixerSettings
        {
            MasterVolume = 1.0,
            BgmVolume = 0.5,
            SfxVolume = 0.25,
            VoiceVolume = 0.0,
            AudioStereoWidthPercent = 0.0,
            ResamplingMode = HostAudioResamplingMode.SincCubic,
            FastForwardAudioMode = HostFastForwardAudioMode.Muted,
            ShouldMuteOnFocusLoss = true
        });

        short[] mixed = new short[4];
        dsp.MixCategories([1000, 2000, 1000, 2000], [400, 400, 400, 400], [8000, 8000, 8000, 8000], mixed);
        Assert.Equal(600, mixed[0]); // 1000*0.5 + 400*0.25 + 0
        Assert.Equal(1100, mixed[1]); // 2000*0.5 + 400*0.25 + 0

        // 0% stereo width collapses stereo L/R into exact mono Mid.
        short[] monoBuf = [2000, -1000, 4000, 1000];
        dsp.ProcessInterleaved(monoBuf);
        Assert.Equal(monoBuf[0], monoBuf[1]);
        Assert.Equal(monoBuf[2], monoBuf[3]);

        // SincCubic resampling produces clean non-zero output.
        short[] src44 = BuildSineStereo(44100, 440.0, frames: 512);
        short[] dst48 = new short[1200];
        int produced = dsp.ResampleInterleavedStereo(src44, 44100, 48000, dst48);
        Assert.True(produced > 1000);
        Assert.True(ComputeRms(dst48.AsSpan(0, produced)) > 100.0);

        // Fast-forward muted zeroes output; focus loss smoothly ramps down across ~5 ms.
        dsp.SetFocusAndFastForward(true, true, 3.0);
        short[] ffMuteBuf = [3000, 3000, -3000, -3000];
        dsp.ProcessInterleaved(ffMuteBuf);
        Assert.All(ffMuteBuf, s => Assert.Equal(0, s));

        dsp.SetFocusAndFastForward(isWindowFocused: false, isFastForwardEnabled: false, 2.0);
        short[] rampBuf = BuildSineStereo(44100, 440.0, frames: 64);
        dsp.ProcessInterleaved(rampBuf);
        Assert.InRange(dsp.CurrentFocusGain, 0.5, 0.99);
        short[] settleBuf = BuildSineStereo(44100, 440.0, frames: 512);
        dsp.ProcessInterleaved(settleBuf);
        Assert.Equal(0.0, dsp.CurrentFocusGain, 3);
    }

    [Fact]
    public void HostSoundTestService_AndSixthAudioDrawerTab_ToggleSoundTestAndMixer()
    {
        var jukebox = new HostSoundTestService(sampleRate: 44100);
        Assert.NotEmpty(jukebox.GetTracks());
        Assert.False(jukebox.IsPlaying);

        string firstId = jukebox.CurrentTrack.TrackId;
        HostSoundTestTrack next = jukebox.NextTrack();
        Assert.NotEqual(firstId, next.TrackId);

        var canvas = new PixelOverlayCanvas(640, 480);
        var mapper = new HostInputMapper();
        var drawer = new F1SettingsDrawerView
        {
            SoundTestService = jukebox,
            IsOpen = true,
            ActiveTab = HostDrawerTabId.Audio
        };

        drawer.Render(canvas, HostDrawerDescriptor.CreateDefault());
        string[] tabs = drawer.GetWidgets().Select(w => w.ControlId).Where(id => id.StartsWith("Tab_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["Tab_Video", "Tab_Audio", "Tab_Controls", "Tab_Keybinds", "Tab_System", "Tab_DiscMedia"], tabs);

        // Toggle Sound Test Play button inside 6th Audio tab.
        Assert.True(drawer.TryHandleClick(0, 0, "Audio_SoundTest_PlayStop", mapper));
        Assert.True(jukebox.IsPlaying);

        short[] pcm = new short[256];
        int rendered = jukebox.RenderInterleavedStereo(pcm);
        Assert.Equal(256, rendered);
        Assert.True(ComputeRms(pcm) > 50.0);

        Assert.True(drawer.TryHandleClick(0, 0, "Audio_Mute", mapper));
        Assert.True(drawer.AudioSettings.IsAudioMuted);

        jukebox.Stop();
        Array.Clear(pcm);
        _ = jukebox.RenderInterleavedStereo(pcm);
        Assert.All(pcm, s => Assert.Equal(0, s));
    }

    private static short[] BuildSineStereo(int sampleRate, double freqHz, int frames)
    {
        short[] buf = new short[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            short v = (short)(Math.Sin((2.0 * Math.PI * freqHz * i) / sampleRate) * 12000.0);
            buf[i * 2] = v;
            buf[(i * 2) + 1] = v;
        }
        return buf;
    }

    private static double ComputeRms(ReadOnlySpan<short> samples)
    {
        double sum = 0.0;
        foreach (short s in samples) sum += (double)s * s;
        return samples.Length == 0 ? 0.0 : Math.Sqrt(sum / samples.Length);
    }
}
