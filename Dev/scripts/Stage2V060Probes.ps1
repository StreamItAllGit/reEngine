#Requires -Version 5.1

function Invoke-Stage2V060Probes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$DebugExe,
        [Parameter(Mandatory)][string]$SandboxRoot,
        [Parameter(Mandatory)][string]$EnginesDir,
        [string]$ArtifactDir = ""
    )

    $probeRoot = Join-Path $SandboxRoot "probe_v060"
    $probeDataDir = Join-Path $probeRoot "data"
    $probeCfg = Join-Path $probeDataDir "config\settings.json"
    $probeLogs = Join-Path $probeRoot "logs"
    $probeEnginesDir = Join-Path $probeRoot "engines"
    New-Item -ItemType Directory -Force $probeDataDir, $probeLogs, $probeEnginesDir | Out-Null

    Write-Host "[7/8] Executing v0.6.0 E2E Probes (WinForms Split JSON, Multi-Backend Filters/21:9, 6th Audio Tab + WAV, Crash Sentinel)..."

    # Probe A: WinForms v0.6.0 controls & Standard 19 split JSON persistence
    $pWin = Get-FreeLoopbackPort; $tWin = [Guid]::NewGuid().ToString("N"); $sWin = $null
    $winProc = Start-Process -FilePath $DebugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "--window", "--data-dir", $probeDataDir, "--config", $probeCfg, "--log-dir", $probeLogs,
        "--engines-dir", $probeEnginesDir, "--automation-port", "$pWin", "--automation-token", $tWin
    )
    try {
        $null = Wait-AutomationHealth -Port $pWin -Token $tWin
        Show-WindowOnTop -Proc $winProc
        $sWin = Start-ExeboxStreamForPort -Port $pWin -Token $tWin -SandboxDir $SandboxRoot
        $tree = Wait-TreeControl -Port $pWin -Token $tWin -ControlId "Chk_PreloadMediaToRam"
        foreach ($cid in @("Cmb_DisplayMode", "Cmb_VideoFilter", "Cmb_AudioFilterPreset", "Chk_AudioMuted")) {
            if ($null -eq (Find-TreeNode -Node $tree.data -ControlId $cid)) { throw "Missing WinForms v0.6.0 control '$cid'" }
        }
        foreach ($clk in @("Chk_PreloadMediaToRam", "Chk_AudioMuted")) {
            $null = Invoke-LoopbackJson -Port $pWin -Token $tWin -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = $clk }
            Start-Sleep -Milliseconds 180
        }
        $shotWin = Join-Path $probeLogs "winforms_v060_controls.png"
        $null = Invoke-LoopbackJson -Port $pWin -Token $tWin -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotWin }
        Assert-NonBlankPng -Path $shotWin -ArtifactDir $ArtifactDir
        $null = Invoke-LoopbackJson -Port $pWin -Token $tWin -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Btn_Close" }
        if (-not $winProc.WaitForExit(10000) -or $winProc.ExitCode -ne 0) { throw "Probe A WinForms exited abnormally ($($winProc.ExitCode))" }
    } finally { Stop-ExeboxStream $sWin; if (-not $winProc.HasExited) { $winProc.Kill() } }

    $videoCfgPath = Join-Path $probeDataDir "config\settings.video.json"
    $audioCfgPath = Join-Path $probeDataDir "config\settings.audio.json"
    foreach ($reqFile in @($probeCfg, $videoCfgPath, $audioCfgPath)) {
        if (-not (Test-Path -LiteralPath $reqFile)) { throw "Standard 19 split config missing: $reqFile" }
    }
    $coreObj = Get-Content -LiteralPath $probeCfg -Raw | ConvertFrom-Json
    $audioObj = Get-Content -LiteralPath $audioCfgPath -Raw | ConvertFrom-Json
    if ($coreObj.schemaVersion -lt 2 -or -not $coreObj.shouldPreloadMediaToRam -or -not $audioObj.isAudioMuted) {
        throw "Probe A Standard 19 settings persistence check failed"
    }

    # Probe B: Multi-backend filters & 21:9 aspect + filteredComposite screenshot capture
    $backendSpecs = @(
        @{ Backend = "opengl"; Extra = @("--video-filter", "crtLite"); File = "sdl_v060_opengl_crt.png" },
        @{ Backend = "vulkan"; Extra = @("--video-filter", "xbrz"); File = "sdl_v060_vulkan_xbrz.png" },
        @{ Backend = "directx"; Extra = @("--video-filter", "ntscComposite"); File = "sdl_v060_directx_ntsc.png" },
        @{ Backend = "parallelCpu"; Extra = @("--aspect-mode", "horPlus21By9", "--screenshot-mode", "filteredComposite"); File = "sdl_v060_cpu_21x9.png" }
    )
    foreach ($spec in $backendSpecs) {
        $outPng = Join-Path $probeLogs $spec.File
        $cliArgs = @(
            "--host-demo", "--backend", $spec.Backend, "--require-backend", "--frames", "3",
            "--dump-screenshot", $outPng, "--data-dir", $probeDataDir, "--config", $probeCfg, "--log-dir", $probeLogs
        ) + $spec.Extra
        $res = Invoke-CliBounded -ExePath $DebugExe -WorkDir $probeRoot -CliArgs $cliArgs
        if ($res.ExitCode -ne 0) { throw "Probe B ($($spec.Backend)) failed ($($res.ExitCode)): $($res.StdErr) $($res.StdOut)" }
        Assert-NonBlankPng -Path $outPng -ArtifactDir $ArtifactDir
    }

    # Probe C: Visible SDL3 6th Audio Tab + Sound Test + WAV capture
    $wavPath = Join-Path $probeLogs "sdl_v060_soundtest.wav"
    $pAud = Get-FreeLoopbackPort; $tAud = [Guid]::NewGuid().ToString("N"); $sAud = $null
    $audProc = Start-Process -FilePath $DebugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "--host-demo", "--backend", "opengl", "--require-backend", "--window", "--dump-audio", $wavPath,
        "--no-audio-muted", "--master-volume", "0.85",
        "--data-dir", $probeDataDir, "--config", $probeCfg, "--log-dir", $probeLogs,
        "--engines-dir", $probeEnginesDir, "--automation-port", "$pAud", "--automation-token", $tAud
    )
    try {
        $null = Wait-AutomationHealth -Port $pAud -Token $tAud
        Show-WindowOnTop -Proc $audProc
        $sAud = Start-ExeboxStreamForPort -Port $pAud -Token $tAud -SandboxDir $SandboxRoot
        $null = Wait-TreeControl -Port $pAud -Token $tAud -ControlId "HostViewport"
        $null = Invoke-LoopbackJson -Port $pAud -Token $tAud -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F1" }
        $null = Wait-TreeControl -Port $pAud -Token $tAud -ControlId "Tab_Audio"
        $null = Invoke-LoopbackJson -Port $pAud -Token $tAud -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Tab_Audio" }
        $null = Wait-TreeControl -Port $pAud -Token $tAud -ControlId "Audio_SoundTest_PlayStop"
        $null = Invoke-LoopbackJson -Port $pAud -Token $tAud -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Audio_SoundTest_PlayStop" }
        Start-Sleep -Milliseconds 600
        $shotAud = Join-Path $probeLogs "sdl_v060_audio_tab_live.png"
        $null = Invoke-LoopbackJson -Port $pAud -Token $tAud -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotAud }
        Assert-NonBlankPng -Path $shotAud -ArtifactDir $ArtifactDir
        $null = Wait-TreeControl -Port $pAud -Token $tAud -ControlId "Tab_System"
        $null = Invoke-LoopbackJson -Port $pAud -Token $tAud -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Tab_System" }
        $null = Wait-TreeControl -Port $pAud -Token $tAud -ControlId "System_QuitGame"
        $null = Invoke-LoopbackJson -Port $pAud -Token $tAud -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "System_QuitGame" }
        if (-not $audProc.WaitForExit(10000) -or $audProc.ExitCode -ne 0) { throw "Probe C SDL3 Audio exited abnormally ($($audProc.ExitCode))" }
    } finally { Stop-ExeboxStream $sAud; if (-not $audProc.HasExited) { $audProc.Kill() } }

    if (-not (Test-Path -LiteralPath $wavPath)) { throw "Probe C WAV capture missing: $wavPath" }
    $wavBytes = [System.IO.File]::ReadAllBytes($wavPath)
    if ($wavBytes.Length -le 44) { throw "Probe C WAV header-only / empty ($($wavBytes.Length) bytes)" }
    $hasNonZeroPcm = $false
    for ($i = 44; $i -lt $wavBytes.Length; $i++) { if ($wavBytes[$i] -ne 0) { $hasNonZeroPcm = $true; break } }
    if (-not $hasNonZeroPcm) { throw "Probe C WAV PCM payload contains only silence" }

    # Probe D: Dead-PID booting.sentinel Crash Recovery & --safe-mode-reset
    $videoBefore = Get-Content -LiteralPath $videoCfgPath -Raw
    $sentinelPath = Join-Path $probeDataDir "booting.sentinel"
    [System.IO.File]::WriteAllText($sentinelPath, '{"pid":99999997,"moduleId":"host-demo","startedAtUtc":"2026-01-01T00:00:00Z"}')
    $pSafe = Get-FreeLoopbackPort; $tSafe = [Guid]::NewGuid().ToString("N"); $sSafe = $null
    $safeProc = Start-Process -FilePath $DebugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "--host-demo", "--backend", "vulkan", "--window",
        "--data-dir", $probeDataDir, "--config", $probeCfg, "--log-dir", $probeLogs,
        "--engines-dir", $probeEnginesDir, "--automation-port", "$pSafe", "--automation-token", $tSafe
    )
    try {
        $null = Wait-AutomationHealth -Port $pSafe -Token $tSafe
        Show-WindowOnTop -Proc $safeProc
        $sSafe = Start-ExeboxStreamForPort -Port $pSafe -Token $tSafe -SandboxDir $SandboxRoot
        $null = Wait-TreeControl -Port $pSafe -Token $tSafe -ControlId "HostViewport"
        $null = Invoke-LoopbackJson -Port $pSafe -Token $tSafe -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F3" }
        $null = Wait-TreeControl -Port $pSafe -Token $tSafe -ControlId "F3Hud"
        Start-Sleep -Milliseconds 350
        $shotSafe = Join-Path $probeLogs "sdl_v060_safemode_live.png"
        $null = Invoke-LoopbackJson -Port $pSafe -Token $tSafe -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotSafe }
        Assert-NonBlankPng -Path $shotSafe -ArtifactDir $ArtifactDir
        $null = Invoke-LoopbackJson -Port $pSafe -Token $tSafe -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F1" }
        $null = Wait-TreeControl -Port $pSafe -Token $tSafe -ControlId "Tab_System"
        $null = Invoke-LoopbackJson -Port $pSafe -Token $tSafe -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Tab_System" }
        $null = Wait-TreeControl -Port $pSafe -Token $tSafe -ControlId "System_QuitGame"
        $null = Invoke-LoopbackJson -Port $pSafe -Token $tSafe -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "System_QuitGame" }
        if (-not $safeProc.WaitForExit(10000) -or $safeProc.ExitCode -ne 0) { throw "Probe D Safe Mode exited abnormally ($($safeProc.ExitCode))" }
    } finally { Stop-ExeboxStream $sSafe; if (-not $safeProc.HasExited) { $safeProc.Kill() } }

    $coreAfterCrash = Get-Content -LiteralPath $probeCfg -Raw | ConvertFrom-Json
    $videoAfterCrash = Get-Content -LiteralPath $videoCfgPath -Raw
    if (-not $coreAfterCrash.isSafeModeActive) { throw "Probe D expected isSafeModeActive=true in settings.json after dead-PID crash boot" }
    if ($videoAfterCrash -ne $videoBefore) { throw "Probe D Safe Mode mutated saved settings.video.json on disk" }

    $resetRes = Invoke-CliBounded -ExePath $DebugExe -WorkDir $probeRoot -CliArgs @(
        "--host-demo", "--safe-mode-reset", "--frames", "1",
        "--data-dir", $probeDataDir, "--config", $probeCfg, "--log-dir", $probeLogs
    )
    if ($resetRes.ExitCode -ne 0) { throw "Probe D --safe-mode-reset failed ($($resetRes.ExitCode))" }
    $coreAfterReset = Get-Content -LiteralPath $probeCfg -Raw | ConvertFrom-Json
    if ($coreAfterReset.isSafeModeActive) { throw "Probe D --safe-mode-reset did not clear isSafeModeActive" }

    Write-Host "PASS: v0.6.0 Probes (WinForms split JSON + 4-backend filters/21:9 + Audio tab Sound Test WAV + Safe Mode) passed."
}
