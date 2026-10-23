[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$ConfirmInteractiveTest,
    [ValidateRange(30, 1800)]
    [int]$TimeoutSeconds = 900,
    [ValidateSet('Wpf', 'WinUI')]
    [string[]]$Candidates = @('Wpf', 'WinUI'),
    [ValidateSet('Bottom', 'Left', 'Right')]
    [string[]]$Edges = @('Bottom', 'Left', 'Right'),
    [ValidateSet('Light', 'Dark')]
    [string[]]$BackdropThemes = @('Light', 'Dark'),
    [ValidateSet(44, 58, 72)]
    [int[]]$IconSizes = @(44, 58, 72),
    [switch]$UseWinUiRoundedRegionCandidate
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

if ($UseWinUiRoundedRegionCandidate -and (@($Candidates | Where-Object { $_ -ne 'WinUI' }).Count -gt 0)) {
    throw '-UseWinUiRoundedRegionCandidate is restricted to a WinUI-only run.'
}

$probeExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
$wgcExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.WgcProbe\bin\Release\net10.0-windows10.0.26100.0\WgcProbe.exe'
if (-not (Test-Path -LiteralPath $probeExe)) { throw "Build the input probe first: $probeExe" }
if (-not (Test-Path -LiteralPath $wgcExe)) { throw "Build the WGC probe first: $wgcExe" }

Confirm-M0InteractiveTest -Confirmed:$ConfirmInteractiveTest -Purpose 'show a full-screen controlled backdrop and topmost Dock windows, then capture DWM-composed frames with Windows.Graphics.Capture'
if (-not $PSCmdlet.ShouldProcess('Current desktop', 'Show controlled full-screen backdrops and Dock windows, capture screenshots, and save them to D:')) { return }

$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$evidenceRoot = Join-Path $script:LocalRoot "evidence\m0-gap\visual\$runId"
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
    -Purpose 'visual matrix: WPF and WinUI 3; light/dark controlled backdrops; Bottom/Left/Right; 44/58/72 DIP; WGC desktop composition capture' `
    -WatchdogExe $probeExe -TimeoutSeconds $TimeoutSeconds

$oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
$oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
$oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
$oldWindowRegion = $env:GLASSDOCK_M0_TEST_ROUNDED_REGION
$rows = [System.Collections.Generic.List[object]]::new()
$runCompleted = $false
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

try {
    foreach ($candidate in $Candidates) {
        $projectFolder = Join-Path $script:RepoRoot "spikes\GlassDock.PoC.$candidate"
        $framework = if ($candidate -eq 'Wpf') { 'net10.0-windows10.0.19041.0' } else { 'net10.0-windows10.0.26100.0\win-x64' }
        $exeName = if ($candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
        $dockExe = Join-Path $projectFolder "bin\Release\$framework\$exeName"
        if (-not (Test-Path -LiteralPath $dockExe)) { throw "Build the solution first; missing PoC executable: $dockExe" }

        foreach ($theme in $BackdropThemes) {
            foreach ($edge in $Edges) {
                foreach ($iconSize in $IconSizes) {
                    Assert-M0InteractiveTestCanContinue $session
                    $scenarioName = "$candidate-$theme-$edge-$iconSize-DIP"
                    $scenarioRoot = Join-Path $evidenceRoot $scenarioName
                    $sinkRoot = Join-Path $scenarioRoot 'sink'
                    $stateRoot = Join-Path $scenarioRoot 'state'
                    New-Item -ItemType Directory -Force -Path $sinkRoot, $stateRoot | Out-Null
                    $settingsName = if ($candidate -eq 'Wpf') { 'wpf-poc-settings.json' } else { 'winui-poc-settings.json' }
                    $settingsPath = Join-Path $stateRoot $settingsName
                    Register-M0InteractiveRestoreFile -Session $session -TargetPath $settingsPath
                    Set-Content -LiteralPath $settingsPath -Value "{`"Edge`":`"$edge`"}" -Encoding utf8

                    $dockProcess = $null
                    $sinkProcess = $null
                    $captureProcess = $null
                    $status = 'FAIL'
                    $failure = ''
                    $imagePath = Join-Path $scenarioRoot 'composed-frame.png'
                    $readinessPath = Join-Path $sinkRoot 'input-probe-ready.json'
                    $dockLogPath = Join-Path $stateRoot 'logs\dock.log'
                    try {
                        $sinkProcess = Start-M0OwnedTestProcess -Session $session -FilePath $probeExe `
                            -ArgumentList @('--theme', $theme, '--output-root', $sinkRoot)
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
                        $env:GLASSDOCK_POC_ICON_SIZE = [string]$iconSize
                        if ($UseWinUiRoundedRegionCandidate) { $env:GLASSDOCK_M0_TEST_ROUNDED_REGION = '1' }
                        else { Remove-Item Env:GLASSDOCK_M0_TEST_ROUNDED_REGION -ErrorAction SilentlyContinue }
                        $dockProcess = Start-M0OwnedTestProcess -Session $session -FilePath $dockExe -WorkingDirectory $projectFolder
                        $activeProcesses.Add($dockProcess)
                        $dockReady = $false
                        for ($attempt = 0; $attempt -lt 150; $attempt++) {
                            Assert-M0InteractiveTestCanContinue $session
                            $dockProcess.Refresh()
                            if ($dockProcess.HasExited) { throw "$candidate exited early (exit=$($dockProcess.ExitCode))." }
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
                                throw 'WGC metadata did not confirm DWM capture and controlled-backdrop marker validation.'
                            }
                            $status = 'PASS'
                        }
                    }
                    catch {
                        $failure = $_.Exception.Message
                        if ((Test-Path -LiteralPath $session.StopPath) -or $session.WatchdogProcess.HasExited) { throw }
                        $status = if ($failure -match 'Foreground|visible test surface|topmost|did not become ready|refused capture') { 'BLOCKED' } else { 'FAIL' }
                    }
                    finally {
                        Stop-CurrentVisualProcesses -Session $session -Processes $activeProcesses
                    }

                    $rows.Add([pscustomobject]@{
                        Candidate = $candidate
                        CandidateVariant = if ($UseWinUiRoundedRegionCandidate) { 'WinUI SetWindowRgn rounded-region opt-in' } else { 'Default' }
                        BackdropTheme = $theme
                        Edge = $edge
                        IconSizeDip = $iconSize
                        PhysicalDisplay = "$([int]$readiness.Bounds.Width)x$([int]$readiness.Bounds.Height)"
                        Dpi = $readiness.Dpi
                        CaptureApi = if ($status -eq 'PASS') { 'Windows.Graphics.Capture / Direct3D11CaptureFramePool' } else { '' }
                        Status = $status
                        Image = if ($status -eq 'PASS') { $imagePath } else { '' }
                        Metadata = if ($status -eq 'PASS') { [IO.Path]::ChangeExtension($imagePath, '.json') } else { '' }
                        Failure = $failure
                    })
                    $readiness = $null
                    $env:GLASSDOCK_POC_STATE_ROOT = $oldStateRoot
                    $env:GLASSDOCK_POC_ITEM_COUNT = $oldItemCount
                    $env:GLASSDOCK_POC_ICON_SIZE = $oldIconSize
                }
            }
        }
    }
    $runCompleted = $true
}
finally {
    Stop-CurrentVisualProcesses -Session $session -Processes $activeProcesses
    foreach ($entry in @(
        @{ Name='GLASSDOCK_POC_STATE_ROOT'; Value=$oldStateRoot },
        @{ Name='GLASSDOCK_POC_ITEM_COUNT'; Value=$oldItemCount },
        @{ Name='GLASSDOCK_POC_ICON_SIZE'; Value=$oldIconSize },
        @{ Name='GLASSDOCK_M0_TEST_ROUNDED_REGION'; Value=$oldWindowRegion }
    )) {
        if ($null -eq $entry.Value) { Remove-Item "Env:$($entry.Name)" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($entry.Name, [string]$entry.Value, 'Process') }
    }
    try { [void](Stop-M0InteractiveTestSession -Session $session -Completed $runCompleted) }
    catch { Write-Warning "Interactive safety restoration failed; inspect $($session.EvidenceRoot): $($_.Exception.Message)" }
}

$csvPath = Join-Path $evidenceRoot 'actual-visual-results.csv'
$jsonPath = Join-Path $evidenceRoot 'actual-visual-results.json'
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
[IO.File]::WriteAllText($jsonPath, ($rows | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$rows | Group-Object Candidate,Status | ForEach-Object {
    [pscustomobject]@{ CandidateStatus=$_.Name; Count=$_.Count }
} | Format-Table -AutoSize
Write-Output "Evidence: $csvPath"
Write-Output "Evidence: $jsonPath"
