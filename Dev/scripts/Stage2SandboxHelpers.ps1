#Requires -Version 5.1

function Initialize-Stage2Win32Types {
    if (-not ("Win32WindowTop" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32WindowTop {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    public static void CloseAllWindowsForPid(int targetPid) {
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            uint pid = 0;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == (uint)targetPid) PostMessage(hWnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint dwThreadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetUserObjectInformationW(IntPtr hObj, int nIndex, StringBuilder pvInfo, uint nLength, out uint lpnLengthNeeded);
    public static string GetCurrentDesktopName() {
        IntPtr hDesk = GetThreadDesktop(GetCurrentThreadId());
        var sb = new StringBuilder(128);
        uint needed = 0;
        if (hDesk != IntPtr.Zero && GetUserObjectInformationW(hDesk, 2, sb, 256, out needed)) return sb.ToString();
        return "Unknown";
    }
}
"@
    }
    Add-Type -AssemblyName System.Drawing
}

function Write-DesktopPreflightInfo {
    $desk = [Win32WindowTop]::GetCurrentDesktopName()
    $udp = Get-NetUDPEndpoint -LocalPort 5004 -ErrorAction SilentlyContinue | Select-Object -First 1
    $bound = $null -ne $udp
    Write-Host "[Preflight] ThreadDesktop='$desk' | UDP:5004Bound=$bound"
    if ($desk -like "exebox-*") {
        Write-Host "[Preflight] Running inside isolated exebox desktop ($desk); NOT launching Test Viewer.lnk inside exebox."
    }
}

function Get-FreeLoopbackPort {
    $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $l.Start()
    $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port
    $l.Stop()
    return $p
}

function Invoke-LoopbackJson([int]$Port, [string]$Token, [string]$Method, [string]$Route, $Body = $null) {
    $headers = @{ Authorization = "Bearer $Token" }
    $uri = "http://127.0.0.1:$Port$Route"
    if ($null -eq $Body) { return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -TimeoutSec 10 }
    $json = $Body | ConvertTo-Json -Compress
    return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -ContentType "application/json" -Body $json -TimeoutSec 10
}

function Wait-AutomationHealth([int]$Port, [string]$Token) {
    for ($i = 0; $i -lt 45; $i++) {
        try {
            $h = Invoke-LoopbackJson -Port $Port -Token $Token -Method "GET" -Route "/v1/health"
            if ($h.isPassed) { return $h }
        } catch { Start-Sleep -Milliseconds 150 }
    }
    throw "Automation loopback failed to become healthy on 127.0.0.1:$Port"
}

function Find-TreeNode($Node, [string]$ControlId) {
    if ($null -eq $Node) { return $null }
    if ($Node.PSObject.Properties["id"] -and $Node.id -eq $ControlId) { return $Node }
    if ($Node.PSObject.Properties["children"] -and $null -ne $Node.children) {
        foreach ($c in $Node.children) {
            $hit = Find-TreeNode -Node $c -ControlId $ControlId
            if ($null -ne $hit) { return $hit }
        }
    }
    return $null
}

function Wait-TreeControl([int]$Port, [string]$Token, [string]$ControlId) {
    for ($i = 0; $i -lt 30; $i++) {
        $tree = Invoke-LoopbackJson -Port $Port -Token $Token -Method "GET" -Route "/v1/tree"
        $hit = Find-TreeNode -Node $tree.data -ControlId $ControlId
        if ($null -ne $hit -and $hit.isVisible) { return $tree }
        Start-Sleep -Milliseconds 120
    }
    throw "Control '$ControlId' did not become visible in /v1/tree on port $Port"
}

function Show-WindowOnTop([System.Diagnostics.Process]$Proc) {
    for ($i = 0; $i -lt 50; $i++) {
        if ($Proc.HasExited) { return }
        $Proc.Refresh()
        if ($Proc.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    }
    $hwnd = $Proc.MainWindowHandle
    if ($hwnd -ne [IntPtr]::Zero) {
        [void][Win32WindowTop]::ShowWindow($hwnd, 9)
        [void][Win32WindowTop]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)
        [void][Win32WindowTop]::SetForegroundWindow($hwnd)
    }
}

function Assert-NonBlankPng([string]$Path, [string]$ArtifactDir = "") {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Screenshot missing: $Path" }
    if ((Get-Item -LiteralPath $Path).Length -lt 1024) { throw "Screenshot too small: $Path" }
    $bmp = [System.Drawing.Bitmap]::new($Path)
    try {
        $first = $bmp.GetPixel(0, 0)
        $hasDiff = $false
        $stepX = [Math]::Max(1, [int]($bmp.Width / 24))
        $stepY = [Math]::Max(1, [int]($bmp.Height / 16))
        for ($y = 0; $y -lt $bmp.Height -and -not $hasDiff; $y += $stepY) {
            for ($x = 0; $x -lt $bmp.Width; $x += $stepX) {
                $px = $bmp.GetPixel($x, $y)
                if ($px.ToArgb() -ne $first.ToArgb()) { $hasDiff = $true; break }
            }
        }
        if (-not $hasDiff) { throw "Captured screenshot appears uniform/blank: $Path" }
    } finally { $bmp.Dispose() }
    if (-not [string]::IsNullOrWhiteSpace($ArtifactDir)) {
        New-Item -ItemType Directory -Force $ArtifactDir | Out-Null
        Copy-Item -LiteralPath $Path -Destination (Join-Path $ArtifactDir (Split-Path -Leaf $Path)) -Force
    }
}

function Invoke-CliBounded([string]$ExePath, [string[]]$CliArgs, [string]$WorkDir, [int]$TimeoutMs = 20000) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ExePath
    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $quotedArgs = foreach ($a in $CliArgs) {
        if ($null -eq $a -or $a -eq "") { '""' }
        elseif ($a -match '[\s"]') { '"' + ($a -replace '(\\*)"', '$1$1\"') + '"' }
        else { $a }
    }
    $psi.Arguments = $quotedArgs -join " "
    $p = [System.Diagnostics.Process]::Start($psi)
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutMs)) {
        try { $p.Kill() } catch { }
        throw "Bounded CLI timed out after ${TimeoutMs}ms: $ExePath $($CliArgs -join ' ')"
    }
    $null = $outTask.Wait(2000)
    $null = $errTask.Wait(2000)
    return [pscustomobject]@{ ExitCode = $p.ExitCode; StdOut = $outTask.Result; StdErr = $errTask.Result }
}

function New-DetachedChildStub([string]$EngineDir, [string]$WorkDir) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { throw "Framework csc.exe missing at $csc" }
    $csPath = Join-Path $WorkDir "MockReStationEngine.cs"
    $stubSource = @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class MockReStationEngine
{
    private static string GetArgValue(string[] args, string key) {
        for (int i = 0; i + 1 < args.Length; i++) if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--child-window")
        {
            string readyFile = args[1];
            string stateDir = Path.GetDirectoryName(readyFile);
            string argsPath = Path.Combine(stateDir, "received_args.txt");
            string[] savedLines = File.Exists(argsPath) ? File.ReadAllLines(argsPath) : Array.Empty<string>();
            string saveDir = GetArgValue(savedLines, "--save-dir");
            bool waitMode = Array.Exists(savedLines, a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase));
            Application.EnableVisualStyles();
            var form = new Form {
                Text = "reStation Sandbox Child Window", Width = 640, Height = 360,
                StartPosition = FormStartPosition.CenterScreen, ShowInTaskbar = true, TopMost = true,
                BackColor = Color.FromArgb(14, 14, 16), ForeColor = Color.FromArgb(56, 189, 248)
            };
            var lbl = new Label {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
                Text = "reStation Sandbox Child Window\r\n\r\n" + string.Join(" ", savedLines)
            };
            form.Controls.Add(lbl);
            form.Shown += delegate { File.WriteAllText(readyFile, Process.GetCurrentProcess().Id.ToString()); };
            form.FormClosing += delegate {
                if (!string.IsNullOrWhiteSpace(saveDir)) {
                    Directory.CreateDirectory(saveDir);
                    File.WriteAllText(Path.Combine(saveDir, "memcard.sav"), "MEMCARD_FLUSHED_OK");
                }
            };
            Application.Run(form);
            return waitMode ? 0 : 0;
        }

        string exePath = Process.GetCurrentProcess().MainModule.FileName;
        string dir = Path.GetDirectoryName(exePath);
        File.WriteAllLines(Path.Combine(dir, "received_args.txt"), args);
        string pidFile = Path.Combine(dir, "child_window.pid");
        if (File.Exists(pidFile)) File.Delete(pidFile);
        bool syncWait = Array.Exists(args, a => string.Equals(a, "--wait", StringComparison.OrdinalIgnoreCase));
        var proc = Process.Start(new ProcessStartInfo {
            FileName = exePath, Arguments = "--child-window \"" + pidFile + "\"", WorkingDirectory = dir, UseShellExecute = false
        });
        if (syncWait && proc != null) { proc.WaitForExit(); return proc.ExitCode; }
        return 0;
    }
}
'@
    [System.IO.File]::WriteAllText($csPath, $stubSource)
    $outExe = Join-Path $EngineDir "reStation.exe"
    $cscOut = & $csc /nologo /target:winexe /out:$outExe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $csPath 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $outExe)) { throw "Failed compiling sandbox child stub ${outExe}: $($cscOut -join ' ')" }
    [System.IO.File]::WriteAllText((Join-Path $EngineDir ".ready"), "{}")
}

function Start-ExeboxStreamForPort {
    param([Parameter(Mandatory)][int]$Port, [Parameter(Mandatory)][string]$Token, [string]$SandboxDir = "", [int]$TargetPid = 0)
    $sharedHelpers = Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) "Foundation\Dev\tools\ExeboxStreamHelpers.ps1"
    if (Test-Path -LiteralPath $sharedHelpers) {
        . $sharedHelpers
        $snapDir = if (-not [string]::IsNullOrWhiteSpace($SandboxDir)) { Join-Path $SandboxDir "stream_tmp" } else { "" }
        return Start-ExeboxLiveVlcStream -AutomationPort $Port -AutomationToken $Token -TargetPid $TargetPid -SnapshotDir $snapDir -UdpPort 5004 -Fps 20
    }
    $streamScript = Join-Path $env:USERPROFILE ".gemini\config\skills\exebox-stream\scripts\start_exebox_vlc_stream.ps1"
    if (-not (Test-Path -LiteralPath $streamScript)) { return $null }
    $argStr = "-NoProfile -ExecutionPolicy Bypass -File `"$streamScript`" -UdpHost 127.0.0.1 -UdpPort 5004 -Fps 20 -AutomationPort $Port -AutomationToken `"$Token`""
    if ($TargetPid -gt 0) { $argStr += " -TargetPid $TargetPid" }
    if (-not [string]::IsNullOrWhiteSpace($SandboxDir)) {
        $streamTmp = Join-Path $SandboxDir "stream_tmp"
        $null = New-Item -ItemType Directory -Force -Path $streamTmp
        $argStr += " -SnapshotDir `"$streamTmp`""
    }
    $psi = [System.Diagnostics.ProcessStartInfo]::new("powershell.exe")
    $psi.Arguments = $argStr
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    return [System.Diagnostics.Process]::Start($psi)
}

function Stop-ExeboxStream {
    param($StreamProcess)
    if ($null -eq $StreamProcess) { return }
    try {
        if (-not $StreamProcess.HasExited) {
            & cmd.exe /c ("taskkill /T /F /PID " + $StreamProcess.Id + " >nul 2>&1")
            $null = $StreamProcess.WaitForExit(2000)
        }
    } catch {}
    try { $StreamProcess.Dispose() } catch {}
}
