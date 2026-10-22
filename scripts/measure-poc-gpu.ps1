param(
    [Parameter(Mandatory)]
    [ValidateSet('Wpf', 'WinUI')]
    [string]$Candidate,
    [ValidateSet(8, 24)]
    [int]$ItemCount = 8,
    [ValidateSet(44, 58, 72)]
    [int]$IconSize = 58,
    [ValidateRange(5, 300)]
    [int]$DurationSeconds = 5
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

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

$evidenceDirectory = Join-Path $script:LocalRoot 'evidence\gpu'
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$csvPath = Join-Path $evidenceDirectory "${Candidate}-${ItemCount}items-${IconSize}dip-$timestamp.csv"

$nativeMethods = @'
using System;
using System.Runtime.InteropServices;
public delegate bool GlassDockGpuEnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
public static class GlassDockGpuNativeMethods
{
    [DllImport("user32.dll")] public static extern bool EnumWindows(GlassDockGpuEnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    public static IntPtr FindVisibleWindow(int targetProcessId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) => { GetWindowThreadProcessId(hwnd, out uint pid); if (pid == targetProcessId && IsWindowVisible(hwnd)) { found = hwnd; return false; } return true; }, IntPtr.Zero);
        return found;
    }
}
'@
Add-Type -TypeDefinition $nativeMethods

$oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
$oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
$oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
$process = $null
try {
    $env:GLASSDOCK_POC_ITEM_COUNT = [string]$ItemCount
    $env:GLASSDOCK_POC_ICON_SIZE = [string]$IconSize
    $env:GLASSDOCK_POC_STATE_ROOT = Join-Path $script:LocalRoot "poc-state\$Candidate"
    $process = Start-Process -FilePath $exePath -WorkingDirectory $projectFolder -PassThru
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
    Start-Sleep -Seconds 3
    $allRows = [System.Collections.Generic.List[object]]::new()
    $counterQueries = @(
        [pscustomobject]@{ Set='GPU Engine'; Path='\GPU Engine(*)\Utilization Percentage' },
        [pscustomobject]@{ Set='GPU Process Memory'; Path='\GPU Process Memory(*)\Dedicated Usage' },
        [pscustomobject]@{ Set='GPU Process Memory'; Path='\GPU Process Memory(*)\Shared Usage' }
    )
    foreach ($query in $counterQueries) {
        $counterName = $query.Path.Substring($query.Path.LastIndexOf('\') + 1)
        $setInfo = Get-Counter -ListSet $query.Set -ErrorAction Stop
        $matchingPaths = @($setInfo.PathsWithInstances | Where-Object {
            $_ -match "(?i)pid_$($process.Id)_" -and $_.EndsWith("\$counterName", [StringComparison]::OrdinalIgnoreCase)
        })
        if ($matchingPaths.Count -eq 0) {
            $allRows.Add([pscustomobject]@{
                TimestampUtc = [DateTime]::UtcNow.ToString('O')
                Candidate = $Candidate
                ItemCount = $ItemCount
                RequestedIconSizeDip = $IconSize
                ProcessId = $process.Id
                CounterSet = $query.Set
                Counter = $counterName
                Instance = ''
                Status = 'NoMatchingProcessCounter'
                Value = $null
            })
            continue
        }

        try {
            $samples = Get-Counter -Counter $matchingPaths -SampleInterval 1 -MaxSamples $DurationSeconds -ErrorAction Stop
            foreach ($sample in $samples.CounterSamples) {
                $allRows.Add([pscustomobject]@{
                    TimestampUtc = $sample.TimeStamp.ToUniversalTime().ToString('O')
                    Candidate = $Candidate
                    ItemCount = $ItemCount
                    RequestedIconSizeDip = $IconSize
                    ProcessId = $process.Id
                    CounterSet = $query.Set
                    Counter = $counterName
                    Instance = $sample.InstanceName
                    Status = $sample.Status
                    Value = if ($sample.Status -eq 0) { [Math]::Round($sample.CookedValue, 4) } else { $null }
                })
            }
        }
        catch {
            $allRows.Add([pscustomobject]@{
                TimestampUtc = [DateTime]::UtcNow.ToString('O')
                Candidate = $Candidate
                ItemCount = $ItemCount
                RequestedIconSizeDip = $IconSize
                ProcessId = $process.Id
                CounterSet = $query.Set
                Counter = $counterName
                Instance = ''
                Status = "SamplingError: $($_.Exception.Message)"
                Value = $null
            })
        }
    }
    $allRows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
    $validRows = @($allRows | Where-Object { $null -ne $_.Value })
    $engineRows = @($validRows | Where-Object CounterSet -eq 'GPU Engine')
    $memoryRows = @($validRows | Where-Object CounterSet -eq 'GPU Process Memory')
    [pscustomobject]@{
        Candidate = $Candidate
        ItemCount = $ItemCount
        RequestedIconSizeDip = $IconSize
        DurationSecondsPerCounterSet = $DurationSeconds
        ProcessId = $process.Id
        EngineSamples = $engineRows.Count
        PeakGpuEngineUtilizationPercent = if ($engineRows.Count) { ($engineRows | Measure-Object Value -Maximum).Maximum } else { $null }
        PeakDedicatedMemoryBytes = if ($memoryRows.Where({$_.Counter -eq 'Dedicated Usage'}).Count) { ($memoryRows.Where({$_.Counter -eq 'Dedicated Usage'}) | Measure-Object Value -Maximum).Maximum } else { $null }
        PeakSharedMemoryBytes = if ($memoryRows.Where({$_.Counter -eq 'Shared Usage'}).Count) { ($memoryRows.Where({$_.Counter -eq 'Shared Usage'}) | Measure-Object Value -Maximum).Maximum } else { $null }
        CsvPath = $csvPath
    } | Format-List
}
finally {
    if ($process -and -not $process.HasExited) {
        $window = [GlassDockGpuNativeMethods]::FindVisibleWindow($process.Id)
        if ($window -ne [IntPtr]::Zero) {
            [void][GlassDockGpuNativeMethods]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        }
        else { Stop-Process -Id $process.Id -Force }
    }
}
