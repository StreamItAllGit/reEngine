// @module: reEngine.Launcher.Program
// @purpose: Clean-root launcher delegating CLI/GUI forwarding to Foundation.Launcher.RootLauncherHost with embedded AssemblyResolve.
// @depends-on: System, System.IO, System.Reflection, System.Runtime.CompilerServices, Foundation.Launcher
// @exports: Program

using System.Reflection;
using System.Runtime.CompilerServices;
using Foundation.Launcher;

namespace reEngine.Launcher;

internal static class Program
{
    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += static (_, e) =>
        {
            if (string.Equals(new AssemblyName(e.Name).Name, "Foundation.Launcher", StringComparison.OrdinalIgnoreCase))
            {
                using Stream? stream = typeof(Program).Assembly.GetManifestResourceStream("Foundation.Launcher.dll");
                if (stream is not null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
            return null;
        };
    }

    [STAThread]
    private static int Main(string[] args) => RunLauncher(args ?? Array.Empty<string>());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunLauncher(string[] args) =>
        RootLauncherHost.Run(Path.Combine("app", "reEngine.exe"), "reEngine", args);
}
