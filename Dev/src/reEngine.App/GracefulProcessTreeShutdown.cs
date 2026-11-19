// @module: reEngine.App.GracefulProcessTreeShutdown
// @purpose: Bounded (<= 3s default) WM_CLOSE graceful teardown across child process trees before JobObject hard kill on --wait cancel.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Runtime.InteropServices, System.Threading.Tasks
// @exports: GracefulProcessTreeShutdown

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace reEngine.App;

internal static class GracefulProcessTreeShutdown
{
    private const uint Th32csSnapProcess = 0x00000002u;
    private const uint WmClose = 0x0010u;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static async Task<bool> RequestShutdownAsync(Process rootProcess, int timeoutMs, Action<string, string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(rootProcess);
        int boundedTimeoutMs = Math.Clamp(timeoutMs, 100, 30000);
        if (rootProcess.HasExited) return true;

        int rootPid = rootProcess.Id;
        HashSet<int> treePids = OperatingSystem.IsWindows()
            ? CollectProcessTreePidsWindows(rootPid)
            : new HashSet<int> { rootPid };

        log?.Invoke("INFO", $"Requesting graceful shutdown across {treePids.Count} process(es) (timeout={boundedTimeoutMs}ms).");
        SendCloseSignals(rootProcess, treePids);

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < boundedTimeoutMs)
        {
            if (AreAllProcessesExited(rootProcess, treePids))
            {
                log?.Invoke("INFO", $"Child process tree exited cleanly in {sw.ElapsedMilliseconds}ms.");
                return true;
            }
            await Task.Delay(25).ConfigureAwait(false);
        }

        log?.Invoke("WARN", $"Graceful shutdown timed out after {boundedTimeoutMs}ms; forcing process tree termination.");
        try
        {
            if (!rootProcess.HasExited) rootProcess.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Trace.WriteLine($"Process tree kill skipped (already exited): {ex.Message}");
        }
        return false;
    }

    private static void SendCloseSignals(Process rootProcess, HashSet<int> treePids)
    {
        try { _ = rootProcess.CloseMainWindow(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Trace.WriteLine($"CloseMainWindow skipped: {ex.Message}");
        }

        if (!OperatingSystem.IsWindows()) return;

        try
        {
            _ = EnumWindows((hwnd, _param) =>
            {
                _ = GetWindowThreadProcessId(hwnd, out uint ownerPid);
                if (treePids.Contains((int)ownerPid))
                {
                    _ = PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Trace.WriteLine($"EnumWindows WM_CLOSE broadcast unavailable: {ex.Message}");
        }
    }

    private static bool AreAllProcessesExited(Process rootProcess, HashSet<int> treePids)
    {
        rootProcess.Refresh();
        if (!rootProcess.HasExited) return false;

        foreach (int pid in treePids)
        {
            if (pid == rootProcess.Id) continue;
            try
            {
                using Process child = Process.GetProcessById(pid);
                if (!child.HasExited) return false;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Trace.WriteLine($"Descendant process {pid} no longer active: {ex.Message}");
            }
        }
        return true;
    }

    private static HashSet<int> CollectProcessTreePidsWindows(int rootPid)
    {
        var result = new HashSet<int> { rootPid };
        var childrenByParent = new Dictionary<int, List<int>>();
        IntPtr snap = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return result;

        try
        {
            var entry = new ProcessEntry32W { dwSize = (uint)Marshal.SizeOf<ProcessEntry32W>() };
            if (!Process32FirstW(snap, ref entry)) return result;

            do
            {
                int pid = (int)entry.th32ProcessID;
                int ppid = (int)entry.th32ParentProcessID;
                if (!childrenByParent.TryGetValue(ppid, out List<int>? list))
                {
                    list = new List<int>();
                    childrenByParent[ppid] = list;
                }
                list.Add(pid);
            }
            while (Process32NextW(snap, ref entry));
        }
        finally
        {
            _ = CloseHandle(snap);
        }

        var queue = new Queue<int>();
        queue.Enqueue(rootPid);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (!childrenByParent.TryGetValue(cur, out List<int>? kids)) continue;
            foreach (int kid in kids)
            {
                if (result.Add(kid)) queue.Enqueue(kid);
            }
        }
        return result;
    }
}
