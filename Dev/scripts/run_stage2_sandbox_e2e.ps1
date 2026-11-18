#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$SkipInstallerSandbox,
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not ("Win32WindowTop" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Win32WindowTop {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$devDir = Join-Path $root "Dev"
$sandboxRoot = Join-Path $env:TEMP ("reengine_vis_e2e_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $sandboxRoot | Out-Null

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

function Wait-TreeControl([int]$Port, [string]$Token, [string]$ControlId) {
    for ($i = 0; $i -lt 25; $i++) {
        $tree = Invoke-LoopbackJson -Port $Port -Token $Token -Method "GET" -Route "/v1/tree"
        if ($tree.data.children | Where-Object { $_.id -eq $ControlId -and $_.isVisible }) { return $tree }
        Start-Sleep -Milliseconds 100
    }
    throw "Control '$ControlId' did not become visible in /v1/tree on port $Port"
}

function Wait-AutomationHealth([int]$Port, [string]$Token) {
    for ($i = 0; $i -lt 40; $i++) {
        try {
            $h = Invoke-LoopbackJson -Port $Port -Token $Token -Method "GET" -Route "/v1/health"
            if ($h.isPassed) { return $h }
        } catch { Start-Sleep -Milliseconds 150 }
    }
    throw "Automation loopback failed to become healthy on 127.0.0.1:$Port"
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
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--child-window")
        {
            string readyFile = args[1];
            string argsPath = Path.Combine(Path.GetDirectoryName(readyFile), "received_args.txt");
            string argsSummary = File.Exists(argsPath) ? string.Join(" ", File.ReadAllLines(argsPath)) : "";
            Application.EnableVisualStyles();
            var form = new Form
            {
                Text = "reStation Sandbox Child Window",
                Width = 640,
                Height = 360,
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = true,
                TopMost = true,
                BackColor = Color.FromArgb(14, 14, 16),
                ForeColor = Color.FromArgb(56, 189, 248)
            };
            var lbl = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
                Text = "reStation Sandbox Child Window — Launched Detached via reEngine --open\r\n\r\nForwarded Args:\r\n" + argsSummary
            };
            form.Controls.Add(lbl);
            form.Shown += delegate
            {
                File.WriteAllText(readyFile, Process.GetCurrentProcess().Id.ToString());
            };
            Application.Run(form);
            return 0;
        }

        string exePath = Process.GetCurrentProcess().MainModule.FileName;
        string stateDir = Path.GetDirectoryName(exePath);
        File.WriteAllLines(Path.Combine(stateDir, "received_args.txt"), args);
        string pidFile = Path.Combine(stateDir, "child_window.pid");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "--child-window \"" + pidFile + "\"",
            WorkingDirectory = stateDir,
            UseShellExecute = false
        };
        Process.Start(psi);
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

$spawnedChildPid = $null
try {
    $buildsExe = Join-Path $root "Builds\v0.3.0\reEngine.exe"
    $debugExe = Join-Path $devDir "src\reEngine.App\bin\Debug\net8.0-windows\reEngine.exe"
    $releaseExe = Join-Path $devDir "src\reEngine.App\bin\Release\net8.0-windows\reEngine.exe"
    if (-not (Test-Path $debugExe)) { throw "Debug automation binary missing: $debugExe" }
    if (-not (Test-Path $releaseExe)) { throw "Release binary missing: $releaseExe" }
    $openExe = if (Test-Path $buildsExe) { $buildsExe } else { $releaseExe }

    if (-not $SkipInstallerSandbox) {
        $installer = Join-Path $root "Releases\v0.3.0\reEngine-Setup-v0.3.0-win-x64.exe"
        $runnerPy = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) "Personal\Foundation\Dev\tools\run_sandbox_e2e.py"
        if (-not (Test-Path $installer)) { throw "Installer missing at $installer" }
        if (-not (Test-Path $runnerPy)) { throw "Stage 2 runner missing at $runnerPy" }
        Write-Host "[1/5] Executing Foundation 12-point 4-drive Stage 2 installer sandbox..."
        $receiptFile = Join-Path $sandboxRoot "stage2_receipt.json"
        & python $runnerPy --app-id reengine --version 0.3.0 --installer $installer "--probe-arg=--version" --receipt-out $receiptFile --timeout-seconds $TimeoutSeconds
        $sbExit = $LASTEXITCODE
        if ($sbExit -eq 3 -and (Test-Path $receiptFile)) {
            $rcp = Get-Content -LiteralPath $receiptFile -Raw | ConvertFrom-Json
            $s01Note = ($rcp.checklistResults | Where-Object { $_.id -eq "S2-01" }).notes
            $nonS12Failed = @($rcp.checklistResults | Where-Object { $_.id -ne "S2-12" -and -not $_.isPassed })
            if ($nonS12Failed.Count -eq 0 -and $s01Note -match '(host-snapshot-[^.\s]+\.json)') {
                $snapName = $Matches[1].Replace("host-snapshot-", "").Replace(".json", "")
                $toolsDir = Split-Path -Parent $runnerPy
                $diffJson = & python -c "import argparse, json, sys; sys.path.insert(0, sys.argv[1]); from sandbox_ctl import resolve_sandbox_config, diff_host_snapshots; cfg = resolve_sandbox_config(argparse.Namespace()); print(json.dumps(diff_host_snapshots(cfg, name=sys.argv[2])))" $toolsDir $snapName
                $diff = $diffJson | ConvertFrom-Json
                $regCount = @($diff.addedRegistryEntries).Count + @($diff.removedRegistryEntries).Count + @($diff.modifiedRegistryEntries).Count
                $allFiles = @($diff.addedFiles) + @($diff.removedFiles) + @($diff.modifiedFiles)
                $realFiles = @($allFiles | Where-Object { $_ -and $_ -notmatch '^[CD]:\\Programs\\\.tmp\.driveupload(?:\\|$)' })
                if ($regCount -eq 0 -and $realFiles.Count -eq 0) {
                    Write-Host "[1/5] S2-01..S2-11 PASSED; S2-12 filtered $($allFiles.Count) external Google Drive .tmp.driveupload entry(s) (0 reEngine/registry diff)."
                    $sbExit = 0
                }
            }
        }
        if ($sbExit -ne 0) { throw "Foundation Stage 2 sandbox runner failed ($sbExit)" }
    }

    $dataDir = Join-Path $sandboxRoot "data"
    $cfgPath = Join-Path $dataDir "config\settings.json"
    $logDir = Join-Path $sandboxRoot "logs"
    $enginesDir = Join-Path $sandboxRoot "engines"
    $engV028 = Join-Path $enginesDir "reStation\v0.2.8"
    $modDir = Join-Path $sandboxRoot "games\SampleRecomp"
    New-Item -ItemType Directory -Force $dataDir, $logDir, $engV028, $modDir | Out-Null

    New-DetachedChildStub -EngineDir $engV028 -WorkDir $sandboxRoot
    $samplePkg = Join-Path $modDir "SampleRecomp.recomp"
    [System.IO.File]::WriteAllText($samplePkg, @"
{
  "schemaVersion": 1,
  "moduleId": "sample-recomp",
  "title": "Sample Recomp",
  "version": "0.2.0",
  "engineId": "reStation",
  "engineVersion": "0.2.8",
  "compatibleEngineTrack": "0.2.x"
}
"@)

    $env:APP_ISOLATED_TEST = "1"
    $env:FOUNDATION_SANDBOX_ROOT = $sandboxRoot

    Write-Host "[2/5] Launching visible WinForms window E2E (on top of screen)..."
    $p1 = Get-FreeLoopbackPort
    $t1 = [Guid]::NewGuid().ToString("N")
    $winProc = Start-Process -FilePath $debugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "--window", "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir, "--engines-dir", $enginesDir, "--automation-port", "$p1", "--automation-token", $t1
    )
    try {
        $null = Wait-AutomationHealth -Port $p1 -Token $t1
        Show-WindowOnTop -Proc $winProc
        Start-Sleep -Milliseconds 650
        $tree1 = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "GET" -Route "/v1/tree"
        if (-not ($tree1.data.children | Where-Object { $_.id -eq "Nav_PillStrip" })) { throw "Missing Nav_PillStrip in visible WinForms tree" }
        foreach ($btn in @("Pill_Engines", "Pill_BringUp")) {
            $null = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = $btn }
            Start-Sleep -Milliseconds 650
        }
        $treeBringUp = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "GET" -Route "/v1/tree"
        if ($treeBringUp.data.children | Where-Object { $_.id -eq "Btn_LaunchHostDemo" }) {
            throw "Unexpected Btn_LaunchHostDemo found in Bring-Up shelf"
        }
        foreach ($btn in @("Pill_Library", "Btn_ToggleDrawer")) {
            $null = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = $btn }
            Start-Sleep -Milliseconds 650
        }
        $shot1 = Join-Path $logDir "winforms_live.png"
        $null = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shot1 }
        if ((Get-Item $shot1).Length -lt 1024) { throw "WinForms live screenshot too small" }
        $null = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Btn_Close" }
        if (-not $winProc.WaitForExit(10000)) { $winProc.Kill(); throw "WinForms visible E2E did not close cleanly" }
        if ($winProc.ExitCode -ne 0) { throw "WinForms visible E2E exited with $($winProc.ExitCode)" }
    } finally { if (-not $winProc.HasExited) { $winProc.Kill() } }

    Write-Host "[3/5] Launching real .recomp package into detached visible child window (--backend opengl --scale 4 --jit-bringup)..."
    $launchProc = Start-Process -FilePath $openExe -WindowStyle Hidden -PassThru -ArgumentList @(
        "--open", $samplePkg, "--backend", "opengl", "--scale", "4", "--jit-bringup",
        "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir, "--engines-dir", $enginesDir
    )
    if (-not $launchProc.WaitForExit(15000)) { $launchProc.Kill(); throw "reEngine --open timed out waiting for handoff" }
    if ($launchProc.ExitCode -ne 0) { throw "reEngine --open exited with $($launchProc.ExitCode)" }

    $pidFile = Join-Path $engV028 "child_window.pid"
    $argsFile = Join-Path $engV028 "received_args.txt"
    for ($w = 0; $w -lt 40; $w++) {
        if (Test-Path $pidFile) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path $pidFile)) { throw "Detached visible child engine window never started or was killed prematurely" }
    $spawnedChildPid = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
    $childProc = Get-Process -Id $spawnedChildPid -ErrorAction Stop
    Show-WindowOnTop -Proc $childProc
    Start-Sleep -Milliseconds 1500
    $childProc.Refresh()
    if ($childProc.HasExited) { throw "Detached child window process ($spawnedChildPid) exited unexpectedly" }
    if ($childProc.MainWindowHandle -eq 0) { throw "Detached child window ($spawnedChildPid) has no visible window handle" }
    $recvArgs = (Get-Content -LiteralPath $argsFile) -join " "
    foreach ($expectedToken in @("--backend", "opengl", "--scale", "4", "--jit-bringup")) {
        if ($recvArgs -notmatch [regex]::Escape($expectedToken)) {
            throw "Expected flag '$expectedToken' missing in child engine args: $recvArgs"
        }
    }
    $childProc.Kill()
    $spawnedChildPid = $null

    Write-Host "[4/5] Launching visible SDL3 host E2E (on top of screen)..."
    $p2 = Get-FreeLoopbackPort
    $t2 = [Guid]::NewGuid().ToString("N")
    $sdlProc = Start-Process -FilePath $debugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "--host-demo", "--window", "--preload-media", "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir, "--engines-dir", $enginesDir, "--automation-port", "$p2", "--automation-token", $t2
    )
    try {
        $null = Wait-AutomationHealth -Port $p2 -Token $t2
        Show-WindowOnTop -Proc $sdlProc
        $null = Wait-TreeControl -Port $p2 -Token $t2 -ControlId "MediaGate_DemoBypassButton"
        Start-Sleep -Milliseconds 650
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "MediaGate_DemoBypassButton" }
        Start-Sleep -Milliseconds 650
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F1" }
        $null = Wait-TreeControl -Port $p2 -Token $t2 -ControlId "Tab_Keybinds"
        Start-Sleep -Milliseconds 650
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F3" }
        $null = Wait-TreeControl -Port $p2 -Token $t2 -ControlId "F3Hud"
        Start-Sleep -Milliseconds 650
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Tab_Keybinds" }
        Start-Sleep -Milliseconds 650
        $shot2 = Join-Path $logDir "sdl_live.png"
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shot2 }
        if ((Get-Item $shot2).Length -lt 1024) { throw "SDL3 host live screenshot too small" }
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "ESC" }
        Start-Sleep -Milliseconds 150
        try { $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "ESC" } } catch [System.Net.WebException] { }
        if (-not $sdlProc.WaitForExit(10000)) { $sdlProc.Kill(); throw "SDL3 visible host E2E did not exit cleanly" }
        if ($sdlProc.ExitCode -ne 0) { throw "SDL3 visible host E2E exited with $($sdlProc.ExitCode)" }
    } finally { if (-not $sdlProc.HasExited) { $sdlProc.Kill() } }

    Write-Host "[5/5] Verifying Release fail-closed automation gate (ExitCode = 2)..."
    $relProc = Start-Process -FilePath $releaseExe -WindowStyle Normal -Wait -PassThru -ArgumentList @("--automation-port", "5199", "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir)
    if ($relProc.ExitCode -ne 2) { throw "Expected Release --automation-port ExitCode 2, got $($relProc.ExitCode)" }
    Write-Host "PASS: Stage 2 Sandbox + Visible WinForms + Real Detached Child Window + Visible SDL3 Host + Release ExitCode=2 verified cleanly."
} finally {
    if ($null -ne $spawnedChildPid) {
        try { $p = Get-Process -Id $spawnedChildPid -ErrorAction SilentlyContinue; if ($p -and -not $p.HasExited) { $p.Kill() } } catch { }
    }
    Remove-Item Env:APP_ISOLATED_TEST, Env:FOUNDATION_SANDBOX_ROOT -ErrorAction SilentlyContinue
    if (Test-Path $sandboxRoot) { Remove-Item $sandboxRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
