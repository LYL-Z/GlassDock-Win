[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$ConfirmInteractiveTest,
    [Parameter(Mandatory)]
    [ValidateSet('Wpf', 'WinUI')]
    [string]$Candidate,
    [ValidateSet(8, 24)]
    [int]$ItemCount = 8,
    [ValidateSet(44, 58, 72)]
    [int]$IconSize = 58,
    [ValidateRange(5, 300)]
    [int]$DurationSeconds = 10
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

$projectFolder = if ($Candidate -eq 'Wpf') {
    Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.Wpf'
}
else {
    Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.WinUI'
}
$framework = if ($Candidate -eq 'Wpf') {
    'net10.0-windows10.0.19041.0'
}
else {
    'net10.0-windows10.0.26100.0\win-x64'
}
$exeName = if ($Candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
$exePath = Join-Path $projectFolder "bin\Release\$framework\$exeName"
if (-not (Test-Path -LiteralPath $exePath)) { throw "Build the solution first; missing PoC executable: $exePath" }

$presentMon = Join-Path $script:RepoRoot '.tools\PresentMon\PresentMon-2.6.0-x64.exe'
if (-not (Test-Path -LiteralPath $presentMon)) { throw "PresentMon is not available at $presentMon" }
$expectedHash = 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
$actualHash = (Get-FileHash -LiteralPath $presentMon -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) { throw "PresentMon hash mismatch: $actualHash" }

$evidenceDirectory = Join-Path $script:LocalRoot 'evidence\frames'
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$csvPath = Join-Path $evidenceDirectory "${Candidate}-${ItemCount}items-${IconSize}dip-$timestamp.csv"
$stdoutPath = Join-Path $evidenceDirectory "PresentMon-$Candidate-$timestamp.stdout.txt"
$stderrPath = Join-Path $evidenceDirectory "PresentMon-$Candidate-$timestamp.stderr.txt"
$watchdogExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
if (-not (Test-Path -LiteralPath $watchdogExe)) { throw "Build the safety watchdog first: $watchdogExe" }
Confirm-M0InteractiveTest $ConfirmInteractiveTest -Purpose "show a topmost $Candidate Dock window and collect PresentMon/ETW frame timing"
if (-not $PSCmdlet.ShouldProcess('Current desktop', 'Launch visible topmost Dock PoC and run PresentMon capture')) { return }
$session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
    -Purpose "visible $Candidate topmost Dock window and PresentMon ETW session; frame timing not assumed until data validates" `
    -WatchdogExe $watchdogExe -TimeoutSeconds ([Math]::Min(1800, $DurationSeconds + 90))
$runCompleted = $false

$oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
$oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
$oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
$process = $null
$capture = $null
try {
    $env:GLASSDOCK_POC_ITEM_COUNT = [string]$ItemCount
    $env:GLASSDOCK_POC_ICON_SIZE = [string]$IconSize
    $env:GLASSDOCK_POC_STATE_ROOT = Join-Path $script:LocalRoot "poc-state\$Candidate"
    $process = Start-M0OwnedTestProcess -Session $session -FilePath $exePath -WorkingDirectory $projectFolder
}
catch {
    try { [void](Stop-M0InteractiveTestSession -Session $session -Completed $false) } catch { }
    throw
}
finally {
    if ($null -eq $oldItemCount) { Remove-Item Env:GLASSDOCK_POC_ITEM_COUNT -ErrorAction SilentlyContinue }
    else { $env:GLASSDOCK_POC_ITEM_COUNT = $oldItemCount }
    if ($null -eq $oldIconSize) { Remove-Item Env:GLASSDOCK_POC_ICON_SIZE -ErrorAction SilentlyContinue }
    else { $env:GLASSDOCK_POC_ICON_SIZE = $oldIconSize }
    if ($null -eq $oldStateRoot) { Remove-Item Env:GLASSDOCK_POC_STATE_ROOT -ErrorAction SilentlyContinue }
    else { $env:GLASSDOCK_POC_STATE_ROOT = $oldStateRoot }
}

try {
    Assert-M0InteractiveTestCanContinue $session
    Start-Sleep -Seconds 3
    $arguments = @(
        '--process_id', [string]$process.Id,
        '--timed', [string]$DurationSeconds,
        '--terminate_after_timed',
        '--output_file', $csvPath,
        '--session_name', "GlassDock${Candidate}$timestamp",
        '--no_console_stats'
    )
    Assert-M0InteractiveTestCanContinue $session
    $capture = Start-Process -FilePath $presentMon -ArgumentList $arguments `
        -WorkingDirectory (Split-Path $presentMon) -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    [void](Register-M0OwnedInteractiveProcess -Session $session -Process $capture)
    if (-not $capture.WaitForExit(($DurationSeconds + 30) * 1000)) {
        [void](Stop-M0OwnedTestProcess $session $capture)
        throw 'PresentMon capture timed out.'
    }
    [void](Stop-M0OwnedTestProcess $session $capture)

    $captureOutput = @()
    if (Test-Path -LiteralPath $stdoutPath) { $captureOutput += Get-Content -LiteralPath $stdoutPath -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $stderrPath) { $captureOutput += Get-Content -LiteralPath $stderrPath -ErrorAction SilentlyContinue }
    if ($capture.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $csvPath)) {
        [pscustomobject]@{Candidate=$Candidate;ProcessId=$process.Id;ExitCode=$capture.ExitCode;CsvPath=$csvPath;Output=($captureOutput -join "`n")} | Format-List
        return
    }

    $frames = Import-Csv -LiteralPath $csvPath
    $gpuRows = @($frames | Where-Object { $_.MsGPUTime -and $_.MsGPUTime -ne 'NA' })
    $displayRows = @($frames | Where-Object { $_.MsBetweenDisplayChange -and $_.MsBetweenDisplayChange -ne 'NA' })
    [pscustomobject]@{
        Candidate = $Candidate
        ItemCount = $ItemCount
        RequestedIconSizeDip = $IconSize
        DurationSeconds = $DurationSeconds
        PresentMonExitCode = $capture.ExitCode
        FrameRows = $frames.Count
        MeanMsBetweenPresents = if ($frames.Count) { [Math]::Round((($frames | Measure-Object MsBetweenPresents -Average).Average), 4) } else { $null }
        P95MsBetweenPresents = if ($frames.Count) { [Math]::Round([double](($frames | Sort-Object { [double]$_.MsBetweenPresents } | Select-Object -Skip ([Math]::Floor($frames.Count * 0.95)) -First 1).MsBetweenPresents), 4) } else { $null }
        GpuMetricRows = $gpuRows.Count
        MeanMsGPUTime = if ($gpuRows.Count) { [Math]::Round((($gpuRows | Measure-Object MsGPUTime -Average).Average), 4) } else { $null }
        DisplayMetricRows = $displayRows.Count
        MeanMsBetweenDisplayChange = if ($displayRows.Count) { [Math]::Round((($displayRows | Measure-Object MsBetweenDisplayChange -Average).Average), 4) } else { $null }
        CsvPath = $csvPath
        DiagnosticOutput = ($captureOutput -join "`n")
    } | Format-List
    $runCompleted = $true
}
finally {
    if ($capture) { try { [void](Stop-M0OwnedTestProcess $session $capture) } catch { Write-Warning "Capture-process cleanup failed: $($_.Exception.Message)" } }
    if ($process) { try { [void](Stop-M0OwnedTestProcess $session $process) } catch { Write-Warning "Dock-process cleanup failed: $($_.Exception.Message)" } }
    try { [void](Stop-M0InteractiveTestSession -Session $session -Completed $runCompleted) }
    catch { Write-Warning "Interactive safety restoration failed; inspect $($session.EvidenceRoot): $($_.Exception.Message)" }
}
