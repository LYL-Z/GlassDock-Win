[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$PlanOnly,
    [switch]$ConfirmInteractiveTest,
    [string]$AuthorizationPhrase,
    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

# This file is intentionally restricted to the user's one approved visual matrix:
# 3 candidates x 2 themes x 3 edges x 58 DIP = 18 captures.
$variants = @(
    [pscustomobject]@{
        Id = 'Wpf-DwmAcrylic'
        Candidate = 'Wpf'
        Name = 'WPF + DWM Acrylic'
        FrameworkGlass = $true
        RoundedRegion = $false
    },
    [pscustomobject]@{
        Id = 'WinUI-DesktopAcrylic'
        Candidate = 'WinUI'
        Name = 'WinUI 3 + Desktop Acrylic'
        FrameworkGlass = $true
        RoundedRegion = $false
    },
    [pscustomobject]@{
        Id = 'WinUI-DesktopAcrylic-SetWindowRgn'
        Candidate = 'WinUI'
        Name = 'WinUI 3 + Desktop Acrylic + SetWindowRgn'
        FrameworkGlass = $true
        RoundedRegion = $true
    }
)
$themes = @('Light', 'Dark')
$edges = @('Bottom', 'Left', 'Right')
$iconSizeDip = 58
$plan = foreach ($variant in $variants) {
    foreach ($theme in $themes) {
        foreach ($edge in $edges) {
            [pscustomobject]@{
                Scene = '{0}-{1}-{2}-{3}-DIP' -f $variant.Id, $theme, $edge, $iconSizeDip
                Candidate = $variant.Name
                Theme = $theme
                Edge = $edge
                IconSizeDip = $iconSizeDip
                FrameworkGlass = $variant.FrameworkGlass
                RoundedRegion = $variant.RoundedRegion
            }
        }
    }
}

if (@($plan).Count -ne 18) { throw "Approved matrix must contain exactly 18 scenes; got $(@($plan).Count)." }
if ($PlanOnly) {
    $plan | Select-Object Scene, Candidate, Theme, Edge, IconSizeDip | Format-Table -AutoSize
    Write-Output 'Approved capture count: 18; scene timeout/watchdog maximum: 300 seconds; captures are never visual PASS by themselves.'
    return
}

if (-not $ConfirmInteractiveTest) {
    throw 'Visual test was not started. This exact 18-scene matrix requires -ConfirmInteractiveTest and the user-provided authorization phrase.'
}
if ($AuthorizationPhrase -cne 'RUN M0 DESKTOP TEST') {
    throw 'Visual test was not started because the exact user authorization phrase was not supplied.'
}

. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

$probeExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
$wgcExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.WgcProbe\bin\Release\net10.0-windows10.0.26100.0\WgcProbe.exe'
if (-not (Test-Path -LiteralPath $probeExe)) { throw "Build the input probe first: $probeExe" }
if (-not (Test-Path -LiteralPath $wgcExe)) { throw "Build the WGC probe first: $wgcExe" }

Confirm-M0InteractiveTest -Confirmed:$true -AuthorizationPhrase:$AuthorizationPhrase -Purpose 'temporarily cover the primary display with controlled Light/Dark backdrops and topmost Dock windows for exactly 18 WGC captures; no input injection or display-setting changes'
if (-not $PSCmdlet.ShouldProcess('Current desktop', 'Show the approved controlled backdrop and Dock windows, capture 18 frames, and save evidence under D:\MacOS\GlassDock-Win\.local')) { return }

$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$evidenceRoot = Join-Path $script:LocalRoot "evidence\m0-gap\visual\$runId"
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
    -Purpose 'approved glass matrix: exactly 18 WGC captures; WPF DWM Acrylic, WinUI 3 Desktop Acrylic, WinUI 3 Desktop Acrylic plus SetWindowRgn; Light/Dark x Bottom/Left/Right x 58 DIP' `
    -WatchdogExe $probeExe -TimeoutSeconds $TimeoutSeconds

$oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
$oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
$oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
$oldWindowRegion = $env:GLASSDOCK_M0_TEST_ROUNDED_REGION
$oldVisualCandidate = $env:GLASSDOCK_M0_VISUAL_CANDIDATE
$rows = [System.Collections.Generic.List[object]]::new()
$runCompleted = $false
$stopReason = ''
$restoreResult = $null
$restoreError = ''
$activeProcesses = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Stop-CurrentVisualProcesses {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[System.Diagnostics.Process]]$Processes)

    for ($index = $Processes.Count - 1; $index -ge 0; $index--) {
        $process = $Processes[$index]
        try {
            $stopResult = Stop-M0OwnedTestProcess -Session $Session -Process $process
            if (-not $stopResult.Stopped) { Write-Warning "Owned process cleanup did not complete for PID=$($process.Id): $($stopResult.Method)" }
        }
        catch { Write-Warning "Owned-process cleanup needs review for PID=$($process.Id): $($_.Exception.Message)" }
        finally { $Processes.RemoveAt($index) }
    }
}

function Restore-VisualEnvironment {
    foreach ($entry in @(
        @{ Name='GLASSDOCK_POC_STATE_ROOT'; Value=$oldStateRoot },
        @{ Name='GLASSDOCK_POC_ITEM_COUNT'; Value=$oldItemCount },
        @{ Name='GLASSDOCK_POC_ICON_SIZE'; Value=$oldIconSize },
        @{ Name='GLASSDOCK_M0_TEST_ROUNDED_REGION'; Value=$oldWindowRegion },
        @{ Name='GLASSDOCK_M0_VISUAL_CANDIDATE'; Value=$oldVisualCandidate }
    )) {
        if ($null -eq $entry.Value) { Remove-Item "Env:$($entry.Name)" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($entry.Name, [string]$entry.Value, 'Process') }
    }
}

try {
    foreach ($scene in $plan) {
        Assert-M0InteractiveTestCanContinue $session
        $variant = $variants | Where-Object { $_.Name -ceq $scene.Candidate } | Select-Object -First 1
        $candidate = $variant.Candidate
        $projectFolder = Join-Path $script:RepoRoot "spikes\GlassDock.PoC.$candidate"
        $framework = if ($candidate -eq 'Wpf') { 'net10.0-windows10.0.19041.0' } else { 'net10.0-windows10.0.26100.0\win-x64' }
        $exeName = if ($candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
        $dockExe = Join-Path $projectFolder "bin\Release\$framework\$exeName"
        if (-not (Test-Path -LiteralPath $dockExe)) { throw "Missing PoC executable for $($scene.Candidate): $dockExe" }

        $scenarioRoot = Join-Path $evidenceRoot $scene.Scene
        $sinkRoot = Join-Path $scenarioRoot 'sink'
        $stateRoot = Join-Path $scenarioRoot 'state'
        New-Item -ItemType Directory -Force -Path $sinkRoot, $stateRoot | Out-Null
        $settingsName = if ($candidate -eq 'Wpf') { 'wpf-poc-settings.json' } else { 'winui-poc-settings.json' }
        $settingsPath = Join-Path $stateRoot $settingsName
        Register-M0InteractiveRestoreFile -Session $session -TargetPath $settingsPath
        Set-Content -LiteralPath $settingsPath -Value "{`"Edge`":`"$($scene.Edge)`"}" -Encoding utf8

        $readiness = $null
        $status = 'FAIL'
        $failure = ''
        $imagePath = Join-Path $scenarioRoot 'composed-frame.png'
        $readinessPath = Join-Path $sinkRoot 'input-probe-ready.json'
        $dockLogPath = Join-Path $stateRoot 'logs\dock.log'
        try {
            $sinkProcess = Start-M0OwnedTestProcess -Session $session -FilePath $probeExe `
                -ArgumentList @('--theme', $scene.Theme, '--output-root', $sinkRoot)
            $activeProcesses.Add($sinkProcess)
            for ($attempt = 0; $attempt -lt 150 -and -not (Test-Path -LiteralPath $readinessPath); $attempt++) {
                Assert-M0InteractiveTestCanContinue $session
                $sinkProcess.Refresh()
                if ($sinkProcess.HasExited) { throw "Backdrop exited early (exit=$($sinkProcess.ExitCode))." }
                Start-Sleep -Milliseconds 100
            }
            if (-not (Test-Path -LiteralPath $readinessPath)) { throw 'Backdrop did not become ready within 15 seconds.' }
            $readiness = Get-Content -LiteralPath $readinessPath -Raw | ConvertFrom-Json
            if (-not $readiness.ForegroundConfirmed -or -not $readiness.ExpectedAreaCovered) { throw 'Backdrop refused capture because foreground or full-monitor coverage was not confirmed.' }

            $env:GLASSDOCK_POC_STATE_ROOT = $stateRoot
            $env:GLASSDOCK_POC_ITEM_COUNT = '8'
            $env:GLASSDOCK_POC_ICON_SIZE = [string]$scene.IconSizeDip
            $env:GLASSDOCK_M0_VISUAL_CANDIDATE = if ($variant.FrameworkGlass) {
                if ($candidate -eq 'Wpf') { 'wpf-dwm-acrylic' } else { 'winui-system-acrylic' }
            } else { '' }
            if ($variant.RoundedRegion) { $env:GLASSDOCK_M0_TEST_ROUNDED_REGION = '1' }
            else { Remove-Item Env:GLASSDOCK_M0_TEST_ROUNDED_REGION -ErrorAction SilentlyContinue }

            $dockProcess = Start-M0OwnedTestProcess -Session $session -FilePath $dockExe -WorkingDirectory $projectFolder
            $activeProcesses.Add($dockProcess)
            $dockReady = $false
            for ($attempt = 0; $attempt -lt 150; $attempt++) {
                Assert-M0InteractiveTestCanContinue $session
                $dockProcess.Refresh()
                if ($dockProcess.HasExited) { throw "$($scene.Candidate) exited early (exit=$($dockProcess.ExitCode))." }
                if (Test-Path -LiteralPath $dockLogPath) {
                    $log = Get-Content -LiteralPath $dockLogPath -Raw
                    if ($log -match 'WPF spike launched|WinUI 3 spike positioned') { $dockReady = $true; break }
                }
                Start-Sleep -Milliseconds 100
            }
            if (-not $dockReady) { throw 'Dock readiness log did not appear within 15 seconds.' }
            Start-Sleep -Milliseconds 300

            $captureProcess = Start-M0OwnedTestProcess -Session $session -FilePath $wgcExe `
                -ArgumentList @('capture', $imagePath, $readinessPath, [string]$dockProcess.Id) `
                -WorkingDirectory (Split-Path -Parent $wgcExe)
            $activeProcesses.Add($captureProcess)
            if (-not $captureProcess.WaitForExit(20000)) { throw 'WGC capture did not exit within 20 seconds.' }
            $captureProcess.Refresh()
            if ($captureProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $imagePath)) {
                $errorPath = [IO.Path]::ChangeExtension($imagePath, '.capture-error.txt')
                $failure = if (Test-Path -LiteralPath $errorPath) { (Get-Content -LiteralPath $errorPath -Raw).Trim() } else { "WGC exit=$($captureProcess.ExitCode); no capture diagnostic file." }
                $status = if ($failure -match 'Foreground|visible test surface|Unexpected topmost|not visible|unsupported') { 'BLOCKED' } else { 'FAIL' }
            }
            else {
                $metadataPath = [IO.Path]::ChangeExtension($imagePath, '.json')
                $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
                if ($metadata.CaptureApi -ne 'Windows.Graphics.Capture / Direct3D11CaptureFramePool' -or -not $metadata.BackdropMarkersValidated) {
                    throw 'WGC metadata did not confirm desktop capture and controlled-backdrop marker validation.'
                }
                # Capture success is evidence collection only; visual acceptance requires human review.
                $status = 'CAPTURED_PENDING_VISUAL_REVIEW'
            }
        }
        catch {
            $failure = $_.Exception.Message
            $watchdogStopped = $false
            try { $watchdogStopped = (Test-Path -LiteralPath $session.StopPath) -or $session.WatchdogProcess.HasExited } catch { $watchdogStopped = $true }
            if ($watchdogStopped) { $status = 'STOPPED' }
            elseif ($failure -match 'Foreground|visible test surface|topmost|did not become ready|refused capture') { $status = 'BLOCKED' }
            else { $status = 'FAIL' }
        }
        finally {
            Stop-CurrentVisualProcesses -Session $session -Processes $activeProcesses
            Restore-VisualEnvironment
        }

        $rows.Add([pscustomobject]@{
            Scene = $scene.Scene
            Candidate = $scene.Candidate
            BackdropTheme = $scene.Theme
            Edge = $scene.Edge
            IconSizeDip = $scene.IconSizeDip
            PhysicalDisplay = if ($readiness) { "$([int]$readiness.Bounds.Width)x$([int]$readiness.Bounds.Height)" } else { '' }
            Dpi = if ($readiness) { $readiness.Dpi } else { '' }
            CaptureApi = if ($status -eq 'CAPTURED_PENDING_VISUAL_REVIEW') { 'Windows.Graphics.Capture / Direct3D11CaptureFramePool' } else { '' }
            Status = $status
            Image = if ($status -eq 'CAPTURED_PENDING_VISUAL_REVIEW') { $imagePath } else { '' }
            Metadata = if ($status -eq 'CAPTURED_PENDING_VISUAL_REVIEW') { [IO.Path]::ChangeExtension($imagePath, '.json') } else { '' }
            Failure = $failure
        })

        if ($status -ne 'CAPTURED_PENDING_VISUAL_REVIEW') {
            $stopReason = "Stopped after $($scene.Scene) with status $status. $failure"
            break
        }
    }
    $runCompleted = $rows.Count -eq @($plan).Count -and @($rows | Where-Object { $_.Status -ne 'CAPTURED_PENDING_VISUAL_REVIEW' }).Count -eq 0
}
catch {
    if ([string]::IsNullOrWhiteSpace($stopReason)) { $stopReason = $_.Exception.Message }
}
finally {
    Stop-CurrentVisualProcesses -Session $session -Processes $activeProcesses
    Restore-VisualEnvironment
    try { $restoreResult = Stop-M0InteractiveTestSession -Session $session -Completed $runCompleted }
    catch { $restoreError = $_.Exception.Message; Write-Warning "Desktop restoration check failed; inspect $($session.EvidenceRoot): $restoreError" }
}

$csvPath = Join-Path $evidenceRoot 'actual-visual-results.csv'
$jsonPath = Join-Path $evidenceRoot 'actual-visual-results.json'
$summaryPath = Join-Path $evidenceRoot 'run-summary.json'
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
[IO.File]::WriteAllText($jsonPath, ($rows | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$restorePassed = $null -ne $restoreResult -and [bool]$restoreResult.RestorePassed -and [string]::IsNullOrWhiteSpace($restoreError)
$summary = [ordered]@{
    RunId = $runId
    StartedAfterExactAuthorization = $true
    ExpectedScenes = 18
    RecordedScenes = $rows.Count
    CapturedPendingVisualReview = @($rows | Where-Object { $_.Status -eq 'CAPTURED_PENDING_VISUAL_REVIEW' }).Count
    FailedOrBlocked = @($rows | Where-Object { $_.Status -notin @('CAPTURED_PENDING_VISUAL_REVIEW') }).Count
    VisualPasses = 0
    OverallCaptureMatrixComplete = $runCompleted
    StopReason = $stopReason
    WatchdogTimeoutSeconds = $TimeoutSeconds
    WatchdogProcessIndependentOfDock = $true
    RestorePassed = $restorePassed
    RestoreError = $restoreError
    ResultsCsv = $csvPath
    ResultsJson = $jsonPath
    SafetyEvidence = Join-Path $session.EvidenceRoot 'post-state.json'
    RawCapturesRemainLocal = $true
}
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$rows | Group-Object Candidate, Status | ForEach-Object { [pscustomobject]@{ CandidateStatus=$_.Name; Count=$_.Count } } | Format-Table -AutoSize
Write-Output "Summary: $summaryPath"
Write-Output "Evidence: $csvPath"
Write-Output "Evidence: $jsonPath"
if ($stopReason) { throw "Visual matrix stopped: $stopReason Results: $summaryPath" }
if (-not $restorePassed) { throw "Visual matrix finished without a passing desktop restoration check. Inspect: $summaryPath" }
