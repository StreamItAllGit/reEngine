// @module: reEngine.Installer.Program
// @purpose: 4-pillar zero-PII single-EXE installer entry point with AssemblyResolve, AllowAutoStart = false, and StandardInstallerHost delegation.
// @depends-on: System, System.IO, System.IO.Compression, System.Linq, System.Reflection, System.Runtime.CompilerServices, Foundation.Installer, reEngine.Core.Bootstrap
// @exports: Program

using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using Foundation.Installer;
using reEngine.Core.Bootstrap;

namespace reEngine.Installer;

internal static class Program
{
    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += static (_, e) =>
        {
            string? name = new AssemblyName(e.Name).Name;
            if (name is "Foundation.Core" or "Foundation.Installer" or "reEngine.Core")
            {
                using Stream? gzStream = typeof(Program).Assembly.GetManifestResourceStream(name + ".dll.gz");
                if (gzStream is not null)
                {
                    using var decompressor = new GZipStream(gzStream, CompressionMode.Decompress);
                    using var ms = new MemoryStream();
                    decompressor.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
            return null;
        };
    }

    [STAThread]
    private static int Main(string[] args) => RunMain(args ?? Array.Empty<string>());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunMain(string[] args)
    {
        InstallerAppDescriptor descriptor = CreateDescriptor();
        string[] cleanArgs = SanitizeArgs(args);
        return StandardInstallerHost.Run(descriptor, cleanArgs, typeof(Program).Assembly);
    }

    internal static InstallerAppDescriptor CreateDescriptor() => new(
        ProductName: ReEnginePackagingConstants.DisplayName,
        DisplayName: ReEnginePackagingConstants.DisplayName,
        Publisher: "StreamItAll",
        Version: ReEnginePackagingConstants.Version,
        MainExecutableRelativePath: "reEngine.exe",
        Subtitle: "Zero-Daemon .recomp Router & Side-by-Side Engine Manager",
        EmbeddedPayloadResourceName: "reEngine.Installer.Embedded.payload.zip",
        BrandAccentHex: ReEnginePackagingConstants.BrandAccentHex,
        AllowAutoStart: ReEnginePackagingConstants.AllowAutoStart);

    internal static string[] SanitizeArgs(string[]? args) =>
        args is null
            ? Array.Empty<string>()
            : args.Where(a => !string.Equals(a?.Trim(), "--autostart", StringComparison.OrdinalIgnoreCase)).ToArray();
}
