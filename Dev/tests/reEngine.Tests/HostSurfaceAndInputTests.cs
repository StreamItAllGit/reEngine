// @module: reEngine.Tests.HostSurfaceAndInputTests
// @purpose: xUnit tests for non-ref HostInputFrame, HostRgbaSurfaceBackend PixelOverlayCanvas/PNG output, Sdl3AudioPump WAV capture, and HostInputMapper Schema A/B/C.
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core, Xunit, reEngine.Host
// @exports: HostSurfaceAndInputTests

using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using reEngine.Host.Input;
using Xunit;

namespace reEngine.Tests;

public sealed class HostSurfaceAndInputTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_host_surface_" + Guid.NewGuid().ToString("N"));

    public HostSurfaceAndInputTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine($"Temp cleanup skipped: {ex.Message}"); }
    }

    [Fact]
    public void HostInputFrame_IsNonRefReadOnlyStruct_CompatibleWithGenericsAndDelegates()
    {
        Assert.False(typeof(HostInputFrame).IsByRefLike);
        Assert.True(typeof(HostInputFrame).IsValueType);

        var frames = new List<HostInputFrame>
        {
            new(1, HostGamepadButtons.South | HostGamepadButtons.DPadUp, leftStickY: -1f, activeSchema: "A"),
            new(2, HostGamepadButtons.East, activeSchema: "C")
        };
        Func<HostInputFrame, HostGamepadButtons> selector = static f => f.DigitalButtons;

        Assert.Equal(2, frames.Count);
        Assert.Equal(HostGamepadButtons.South | HostGamepadButtons.DPadUp, selector(frames[0]));
    }

    [Fact]
    public void HostRgbaSurfaceBackend_PresentsAndCapturesValidPngViaFoundationWriter()
    {
        using var surface = new HostRgbaSurfaceBackend(surfaceWidth: 160, surfaceHeight: 120, scaleFactor: 2);
        byte[] content = new byte[64 * 48 * 4];
        for (int i = 0; i < content.Length; i += 4)
        {
            content[i] = 40;
            content[i + 1] = 180;
            content[i + 2] = 240;
            content[i + 3] = 255;
        }

        surface.BeginFrame(64, 48);
        surface.PresentRgba(64, 48, content);
        Assert.True(surface.TryCopyRgbaFrame(out int w, out int h, out byte[] copy));
        Assert.Equal(160, w);
        Assert.Equal(120, h);
        Assert.Equal(160 * 120 * 4, copy.Length);

        string pngPath = Path.Combine(_tempRoot, "capture.png");
        surface.SavePng(pngPath);
        byte[] pngBytes = File.ReadAllBytes(pngPath);
        Assert.True(pngBytes.Length > 64);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], pngBytes[..4]);
    }

    [Fact]
    public void HostInputMapper_MapsSchemaABC_AndTracksEdgeHotkeys()
    {
        var mapper = new HostInputMapper { ActiveSchema = HostInputSchemaKind.SchemaA };
        mapper.InjectSyntheticKey("F1");
        mapper.SetScancodeState(HostScancodes.Z, true);
        HostInputFrame frameA = mapper.BuildFrame();
        Assert.True(frameA.DigitalButtons.HasFlag(HostGamepadButtons.South));
        Assert.True((frameA.HotkeyFlags & (uint)HostHotkeyFlags.ToggleDrawer) != 0);

        mapper.ActiveSchema = HostInputSchemaKind.SchemaC;
        mapper.SetScancodeState(HostScancodes.Space, true);
        mapper.SetScancodeState(HostScancodes.K, true);
        HostInputFrame frameC = mapper.BuildFrame();
        Assert.True(frameC.DigitalButtons.HasFlag(HostGamepadButtons.South));
        Assert.Equal(1f, frameC.TriggerLeft);
        Assert.Equal("C", frameC.ActiveSchema);
        Assert.Equal(0u, frameC.HotkeyFlags);
    }

    [Fact]
    public void Sdl3AudioPump_CapturesValidRiffWaveFileThroughAudioRingBuffer()
    {
        string wavPath = Path.Combine(_tempRoot, "audio.wav");
        using (var pump = new Sdl3AudioPump(new HostAudioFormat(44100, 2, 8192), wavPath, openNativeStream: false))
        {
            short[] samples = new short[512];
            for (int i = 0; i < samples.Length; i++) samples[i] = (short)((i % 32) * 512);
            int written = pump.EnqueueInterleaved(samples);
            Assert.Equal(512, written);
            Assert.True(pump.FillRatio > 0.0);
        }

        byte[] wav = File.ReadAllBytes(wavPath);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.True(wav.Length > 44);
    }
}
