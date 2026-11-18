// @module: reEngine.Tests.HostReleaseAutomationStripTests
// @purpose: xUnit proof that Release reEngine.Host.dll strips Foundation.Automation/WinForms symbols and fails closed (ExitCode=2).
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Reflection.Metadata, System.Reflection.PortableExecutable, System.Threading.Tasks, Xunit, reEngine.Host
// @exports: HostReleaseAutomationStripTests

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using Xunit;

namespace reEngine.Tests;

public sealed class HostReleaseAutomationStripTests
{
    [Fact]
    public void ReleaseHostBinary_HasZeroAutomationOrWinFormsAssemblyReferences()
    {
        string? repoDevDir = FindRepoDevDirectory();
        Assert.NotNull(repoDevDir);

        string releaseNet8Host = Path.Combine(repoDevDir!, "src", "reEngine.Host", "bin", "Release", "net8.0", "reEngine.Host.dll");
        if (!File.Exists(releaseNet8Host))
        {
            releaseNet8Host = Path.Combine(repoDevDir!, "src", "reEngine.Host", "bin", "Release", "net8.0-windows", "reEngine.Host.dll");
        }
        Assert.True(File.Exists(releaseNet8Host), $"Expected compiled Release reEngine.Host.dll at '{releaseNet8Host}'.");

        List<string> refs = ReadAssemblyReferences(releaseNet8Host);
        Assert.DoesNotContain(refs, r => r.Equals("Foundation.Automation", StringComparison.OrdinalIgnoreCase));
        if (releaseNet8Host.Contains($"{Path.DirectorySeparatorChar}net8.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            Assert.DoesNotContain(refs, r => r.Equals("System.Windows.Forms", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(refs, r => r.Equals("Foundation.UI", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task HostSessionRunner_RendersHeadlessFrames_AndFailsClosedInReleaseWhenAutomationRequested()
    {
        var runner = new HostSessionRunner(640, 480);
        var callbacks = new MinimalCallbacks();

#if !REENGINE_ENABLE_AUTOMATION
        HostSessionResult failClosed = await runner.RunAsync(
            new HostSessionOptions { AutomationPort = 5199, RequireVisibleWindow = false, HeadlessFrameCount = 2 },
            callbacks);
        Assert.Equal(2, failClosed.ExitCode);
        Assert.Equal(0, failClosed.FramesRendered);
#else
        HostSessionResult headlessOk = await runner.RunAsync(
            new HostSessionOptions { RequireVisibleWindow = false, HeadlessFrameCount = 3, TargetFps = 0 },
            callbacks);
        Assert.Equal(0, headlessOk.ExitCode);
        Assert.Equal(3, headlessOk.FramesRendered);
#endif
    }

    private static List<string> ReadAssemblyReferences(string assemblyPath)
    {
        using FileStream fs = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(fs);
        MetadataReader mdReader = peReader.GetMetadataReader();
        var names = new List<string>();
        foreach (AssemblyReferenceHandle handle in mdReader.AssemblyReferences)
        {
            AssemblyReference aref = mdReader.GetAssemblyReference(handle);
            names.Add(mdReader.GetString(aref.Name));
        }
        return names;
    }

    private static string? FindRepoDevDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "reEngine.slnx");
            if (File.Exists(candidate)) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private sealed class MinimalCallbacks : IHostSessionCallbacks
    {
        public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct)
        {
            rgbaFramebuffer.Fill(0x40);
            return ValueTask.CompletedTask;
        }

        public int ReadAudioSamples(Span<short> interleavedStereoPcm) => 0;
    }
}
