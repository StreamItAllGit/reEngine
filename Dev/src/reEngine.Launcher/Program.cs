// @module: reEngine.Launcher.Program
// @purpose: Clean-root launcher resolving app\reEngine.exe via Foundation.Launcher with sync-wait, caller-CWD path normalization, and exit code propagation.
// @depends-on: System, System.ComponentModel, System.Diagnostics, System.IO, System.Reflection, System.Runtime.CompilerServices, System.Runtime.InteropServices, System.Threading, Foundation.Launcher
// @exports: Program

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation.Launcher;

namespace reEngine.Launcher;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

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
    private static int RunLauncher(string[] rawArgs)
    {
        string rootDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? selfPath = Environment.ProcessPath;
        string appExe = RootLauncherHost.ResolveTargetExecutable(rootDir, Path.Combine("app", "reEngine.exe"), selfPath);

        if (!string.IsNullOrWhiteSpace(selfPath) &&
            string.Equals(Path.GetFullPath(appExe), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase))
        {
            AttachParentConsoleIfInteractive();
            Console.Error.WriteLine($"Fail-closed self-spawn guard: refusing to launch '{appExe}' from itself.");
            return 1;
        }

        if (!File.Exists(appExe))
        {
            AttachParentConsoleIfInteractive();
            Console.Error.WriteLine($"reEngine application binary not found at '{appExe}'.");
            return 1;
        }

        bool waitForChild = LauncherArgsHelper.ShouldWaitForChild(rawArgs);
        if (waitForChild)
        {
            AttachParentConsoleIfInteractive();
        }

        string[] forwardedArgs = LauncherArgsHelper.NormalizePathArguments(rawArgs, Environment.CurrentDirectory);
        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = Path.GetDirectoryName(appExe) ?? rootDir,
            UseShellExecute = false
        };
        foreach (string arg in forwardedArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            Console.Error.WriteLine($"Failed to start reEngine application at '{appExe}'.");
            return 1;
        }

        if (!waitForChild)
        {
            return 0;
        }

        using var jobGuard = new LauncherJobObjectGuard();
        _ = jobGuard.TryAttachProcess(proc);

        int cancelled = 0;
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            Interlocked.Exchange(ref cancelled, 1);
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            while (!proc.WaitForExit(100))
            {
                if (Volatile.Read(ref cancelled) == 1)
                {
                    if (!proc.WaitForExit(4000) && !proc.HasExited)
                    {
                        try { proc.Kill(entireProcessTree: true); }
                        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                        {
                            Trace.WriteLine($"Launcher cancel teardown skipped already-exited child: {ex.Message}");
                        }
                    }
                    break;
                }
            }
            return Volatile.Read(ref cancelled) == 1 ? 130 : proc.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void AttachParentConsoleIfInteractive()
    {
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
        {
            _ = AttachConsole(AttachParentProcess);
        }
    }
}
