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
    [switch]$EnableM0Telemetry,
    [ValidateRange(5, 300)]
    [int]$DurationSeconds = 15
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
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Build the solution first; missing PoC executable: $exePath"
}

$evidenceDirectory = Join-Path $script:LocalRoot 'evidence\measurements'
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$fileName = "${Candidate}-${ItemCount}items-${IconSize}dip-$timestamp.csv"
$csvPath = Join-Path $evidenceDirectory $fileName
$nativeMethods = @'
using System;
using System.Runtime.InteropServices;
public delegate bool GlassDockEnumWindowsCallback(IntPtr window, IntPtr parameter);
public static class GlassDockMeasurementNativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(GlassDockEnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    public static IntPtr FindVisibleTopLevelWindow(int targetProcessId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId == targetProcessId && IsWindowVisible(window))
            {
                found = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
Add-Type -TypeDefinition $nativeMethods

$watchdogExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
if (-not (Test-Path -LiteralPath $watchdogExe)) { throw "Build the safety watchdog first: $watchdogExe" }
Confirm-M0InteractiveTest -Confirmed:$ConfirmInteractiveTest -Purpose "show a topmost $Candidate Dock window for idle resource sampling"
if (-not $PSCmdlet.ShouldProcess('Current desktop', 'Launch visible topmost Dock PoC and collect local idle resource samples')) { return }
$session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
    -Purpose "visible $Candidate topmost Dock window; CPU/memory samples only; no frame-time claim" `
    -WatchdogExe $watchdogExe -TimeoutSeconds ([Math]::Min(1800, $DurationSeconds + 60))
$runCompleted = $false

$oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
$oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
$oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
$oldPerfTrace = $env:GLASSDOCK_M0_PERF_TRACE
$process = $null
try {
    $env:GLASSDOCK_POC_ITEM_COUNT = [string]$ItemCount
    $env:GLASSDOCK_POC_ICON_SIZE = [string]$IconSize
    $env:GLASSDOCK_POC_STATE_ROOT = Join-Path $script:LocalRoot "evidence\m0-gap\performance\$timestamp\$Candidate-$ItemCount-items-$IconSize-dip"
    $performanceTraceDirectory = Join-Path $env:GLASSDOCK_POC_STATE_ROOT 'performance'
    if ($EnableM0Telemetry) { $env:GLASSDOCK_M0_PERF_TRACE = '1' }
    else { Remove-Item Env:GLASSDOCK_M0_PERF_TRACE -ErrorAction SilentlyContinue }
    $process = Start-M0OwnedTestProcess -Session $session -FilePath $exePath -WorkingDirectory $projectFolder
}
catch {
    try { [void](Stop-M0InteractiveTestSession -Session $session -Completed $false) } catch { }
    throw
}
finally {
    $env:GLASSDOCK_POC_ITEM_COUNT = $oldItemCount
    $env:GLASSDOCK_POC_ICON_SIZE = $oldIconSize
    $env:GLASSDOCK_POC_STATE_ROOT = $oldStateRoot
}

try {
    $windowReady = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $process.Refresh()
        if ($process.HasExited) { throw "$Candidate PoC exited during startup (code $($process.ExitCode))." }
        $window = [GlassDockMeasurementNativeMethods]::FindVisibleTopLevelWindow($process.Id)
        if ($window -ne [IntPtr]::Zero) { $windowReady = $true; break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $windowReady) { throw "$Candidate PoC did not expose a top-level window within 10 seconds." }

    # Discard startup work before measuring idle resource use.
    Start-Sleep -Seconds 3
    $logicalProcessorCount = [Environment]::ProcessorCount
    $process.Refresh()
    $previousCpuSeconds = $process.TotalProcessorTime.TotalSeconds
    $previousTime = [DateTime]::UtcNow
    $startedAt = $previousTime
    $rows = [System.Collections.Generic.List[object]]::new()

    while (([DateTime]::UtcNow - $startedAt).TotalSeconds -lt $DurationSeconds) {
        Assert-M0InteractiveTestCanContinue $session
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited) { throw "$Candidate PoC exited while being measured." }
        $currentTime = [DateTime]::UtcNow
        $cpuSeconds = $process.TotalProcessorTime.TotalSeconds
        $elapsedSeconds = ($currentTime - $previousTime).TotalSeconds
        $cpuPercentOfMachine = if ($elapsedSeconds -gt 0) {
            (($cpuSeconds - $previousCpuSeconds) / $elapsedSeconds / $logicalProcessorCount) * 100
        }
        else { 0 }

        $rows.Add([pscustomobject]@{
            TimestampUtc = $currentTime.ToString('O')
            Candidate = $Candidate
            ItemCount = $ItemCount
            RequestedIconSizeDip = $IconSize
            CpuPercentOfMachine = [Math]::Round($cpuPercentOfMachine, 4)
            CpuSeconds = [Math]::Round($cpuSeconds, 4)
            WorkingSetBytes = $process.WorkingSet64
            PrivateBytes = $process.PrivateMemorySize64
            ThreadCount = $process.Threads.Count
            HandleCount = $process.HandleCount
        })

        $previousCpuSeconds = $cpuSeconds
        $previousTime = $currentTime
    }

    $rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
    $meanCpu = ($rows | Measure-Object -Property CpuPercentOfMachine -Average).Average
    $peakWorkingSet = ($rows | Measure-Object -Property WorkingSetBytes -Maximum).Maximum
    $peakPrivateBytes = ($rows | Measure-Object -Property PrivateBytes -Maximum).Maximum
    [pscustomobject]@{
        Candidate = $Candidate
        Samples = $rows.Count
        DurationSeconds = $DurationSeconds
        MeanCpuPercentOfMachine = [Math]::Round($meanCpu, 4)
        PeakWorkingSetBytes = $peakWorkingSet
        PeakPrivateBytes = $peakPrivateBytes
        CsvPath = $csvPath
        AppTelemetry = if ($EnableM0Telemetry) { "Opt-in app-side logic/render-callback/animation samples; JSONL directory: $performanceTraceDirectory. Not GPU submit, process Present, DWM, or panel scanout." } else { 'Disabled; use -EnableM0Telemetry for framework callbacks and animation timing.' }
        FrameTime = 'No final presented-frame or physical scanout measurement is produced by this script.'
    } | Format-List
    $runCompleted = $true
}
finally {
    if ($process) {
        try { [void](Stop-M0OwnedTestProcess $session $process) }
        catch { Write-Warning "Owned-process cleanup failed: $($_.Exception.Message)" }
    }
    if ($null -eq $oldPerfTrace) { Remove-Item Env:GLASSDOCK_M0_PERF_TRACE -ErrorAction SilentlyContinue }
    else { $env:GLASSDOCK_M0_PERF_TRACE = $oldPerfTrace }
    try { [void](Stop-M0InteractiveTestSession -Session $session -Completed $runCompleted) }
    catch { Write-Warning "Interactive safety restoration failed; inspect $($session.EvidenceRoot): $($_.Exception.Message)" }
}
