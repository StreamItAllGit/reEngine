Set-StrictMode -Version Latest

function Get-NodeLabel {
    param([Parameter(Mandatory)][object]$Node)
    if ($Node.PSObject.Properties['name'] -and -not [string]::IsNullOrWhiteSpace([string]$Node.name)) { return [string]$Node.name }
    if ($Node.PSObject.Properties['text'] -and -not [string]::IsNullOrWhiteSpace([string]$Node.text)) { return [string]$Node.text }
    return ""
}

function Find-TreeNodes {
    param([Parameter(Mandatory)][object]$Node, [System.Collections.Generic.List[object]]$Acc)
    if ($null -eq $Acc) { $Acc = [System.Collections.Generic.List[object]]::new() }
    if ($null -eq $Node) { return $Acc }
    $Acc.Add($Node)
    if ($Node.PSObject.Properties['children'] -and $Node.children) {
        foreach ($child in $Node.children) { $null = Find-TreeNodes -Node $child -Acc $Acc }
    }
    return $Acc
}

function Invoke-Stage2V061ParityProbes {
    param(
        [Parameter(Mandatory)][string]$DebugExe,
        [Parameter(Mandatory)][string]$SandboxRoot,
        [Parameter(Mandatory)][string]$EnginesDir,
        [string]$ArtifactDir = ""
    )

    Write-Host "[7.5/8] Running v0.6.1 R08 parity Stage 2 probes (demo-cart, Glass Media Gate, Scaled Controls/Keybinds, non-quitting Esc, Quit Game)..."
    $pRoot = Join-Path $SandboxRoot "probe-v061"
    $pData = Join-Path $pRoot "data"
    $pLogs = Join-Path $pRoot "logs"
    $pEngines = Join-Path $pRoot "engines"
    $pCfg = Join-Path $pData "config\settings.json"
    New-Item -ItemType Directory -Path (Join-Path $pData "config"), $pLogs, $pEngines -Force | Out-Null

    # 1. --demo-cart clean silent idle WAV check
    $demoWav = Join-Path $pLogs "demo_cart_idle.wav"
    $demoRes = Invoke-CliBounded -ExePath $DebugExe -WorkDir $pRoot -CliArgs @(
        "--demo-cart", "--frames", "12", "--dump-audio", $demoWav,
        "--data-dir", $pData, "--config", $pCfg, "--log-dir", $pLogs, "--engines-dir", $pEngines
    )
    if ($demoRes.ExitCode -ne 0) { throw "v0.6.1 --demo-cart idle check failed ($($demoRes.ExitCode))" }
    if (-not (Test-Path -LiteralPath $demoWav) -or (Get-Item -LiteralPath $demoWav).Length -le 44) {
        throw "v0.6.1 --demo-cart idle WAV missing or empty: $demoWav"
    }

    # 2. Live visible SDL3 R08 parity walkthrough
    $port = Get-FreeLoopbackPort; $token = [Guid]::NewGuid().ToString("N"); $stream = $null
    $proc = Start-Process -FilePath $DebugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "--host-demo", "--preload-media", "--backend", "vulkan", "--window",
        "--fps-mode", "enhanced", "--target-fps", "120", "--ui-text-scale", "1.25",
        "--data-dir", $pData, "--config", $pCfg, "--log-dir", $pLogs, "--engines-dir", $pEngines,
        "--automation-port", "$port", "--automation-token", $token
    )
    try {
        $null = Wait-AutomationHealth -Port $port -Token $token
        Show-WindowOnTop -Proc $proc
        $stream = Start-ExeboxStreamForPort -Port $port -Token $token -SandboxDir $SandboxRoot
        $null = Wait-TreeControl -Port $port -Token $token -ControlId "MediaGate_LaunchDemoButton"
        $gateNodes = Find-TreeNodes (Invoke-LoopbackJson -Port $port -Token $token -Method "GET" -Route "/v1/tree").data
        if ($gateNodes | Where-Object { (Get-NodeLabel $_) -match 'Continue' }) { throw "Media Gate still renders forbidden 'Continue' label!" }
        Start-Sleep -Milliseconds 350
        $shotGate = Join-Path $pLogs "sdl_v061_mediagate_live.png"
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotGate }
        Assert-NonBlankPng -Path $shotGate -ArtifactDir $ArtifactDir

        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "MediaGate_LaunchDemoButton" }
        Start-Sleep -Milliseconds 200
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "ESC" }
        Start-Sleep -Milliseconds 150
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "ESC" }
        Start-Sleep -Milliseconds 150
        $health = Invoke-LoopbackJson -Port $port -Token $token -Method "GET" -Route "/v1/health"
        if ($proc.HasExited -or -not $health.isPassed) { throw "Non-quitting Esc invariant violated in v0.6.1 probe!" }

        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F1" }
        $null = Wait-TreeControl -Port $port -Token $token -ControlId "Video_UiTextScale_Inc"
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Video_UiTextScale_Inc" }
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Tab_Controls" }
        $null = Wait-TreeControl -Port $port -Token $token -ControlId "Controls_Profile_ModernPc"
        $ctrlNodes = Find-TreeNodes (Invoke-LoopbackJson -Port $port -Token $token -Method "GET" -Route "/v1/tree").data
        if ($ctrlNodes | Where-Object { (Get-NodeLabel $_) -match '\bSchema\s+[ABC]\b' }) { throw "Controls tab still contains raw 'Schema A/B/C' text!" }
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Controls_MenuChord" }
        Start-Sleep -Milliseconds 300
        $shotCtrl = Join-Path $pLogs "sdl_v061_controls_scaled_live.png"
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotCtrl }
        Assert-NonBlankPng -Path $shotCtrl -ArtifactDir $ArtifactDir

        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Tab_Keybinds" }
        $null = Wait-TreeControl -Port $port -Token $token -ControlId "Keybinds_ScrollDown"
        for ($s = 0; $s -lt 16; $s++) {
            $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Keybinds_ScrollDown" }
        }
        $null = Wait-TreeControl -Port $port -Token $token -ControlId "Keybinds_Slot_toggleMenu_0"
        $shotKeys = Join-Path $pLogs "sdl_v061_keybinds_host_live.png"
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotKeys }
        Assert-NonBlankPng -Path $shotKeys -ArtifactDir $ArtifactDir

        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "ESC" }
        Start-Sleep -Milliseconds 150
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F3" }
        $null = Wait-TreeControl -Port $port -Token $token -ControlId "F3Hud"
        Start-Sleep -Milliseconds 250
        $shotHud = Join-Path $pLogs "sdl_v061_f3_hud_badges_live.png"
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotHud }
        Assert-NonBlankPng -Path $shotHud -ArtifactDir $ArtifactDir

        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F1" }
        $null = Wait-TreeControl -Port $port -Token $token -ControlId "Tab_System"
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Tab_System" }
        $null = Wait-TreeControl -Port $port -Token $token -ControlId "System_ToggleCleanCapture"
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "System_ToggleCleanCapture" }
        Start-Sleep -Milliseconds 250
        $cleanNodes = Find-TreeNodes (Invoke-LoopbackJson -Port $port -Token $token -Method "GET" -Route "/v1/tree").data
        if ($cleanNodes | Where-Object { $_.PSObject.Properties['id'] -and $_.id -eq "F3Hud" }) { throw "Clean-Capture Mode did not suppress F3Hud!" }
        $shotClean = Join-Path $pLogs "sdl_v061_clean_capture_live.png"
        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotClean }
        Assert-NonBlankPng -Path $shotClean -ArtifactDir $ArtifactDir

        $null = Invoke-LoopbackJson -Port $port -Token $token -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "System_QuitGame" }
        if (-not $proc.WaitForExit(10000) -or $proc.ExitCode -ne 0) { throw "System_QuitGame exited abnormally ($($proc.ExitCode))" }
    } finally { Stop-ExeboxStream $stream; if (-not $proc.HasExited) { $proc.Kill() } }

    $ctrlPath = Join-Path $pData "config\settings.controls.json"
    if (-not (Test-Path -LiteralPath $ctrlPath)) { throw "Missing persisted controls file: $ctrlPath" }
    $ctrlJson = Get-Content -LiteralPath $ctrlPath -Raw | ConvertFrom-Json
    if ($ctrlJson.menuControllerChord -ne "select+start") { throw "Expected menuControllerChord 'select+start', got '$($ctrlJson.menuControllerChord)'" }
    Write-Host "PASS: v0.6.1 R08 parity probes (Media Gate, Scaled UI, Controls/Keybinds, F3 badges + Clean-Capture, non-quitting Esc, Quit Game, controls persistence) passed."
}
