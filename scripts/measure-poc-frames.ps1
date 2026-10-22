param(
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

$oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
$oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
$oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
$process = $null
$capture = $null
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
    $arguments = @(
        '--process_id', [string]$process.Id,
        '--timed', [string]$DurationSeconds,
        '--terminate_after_timed',
        '--output_file', $csvPath,
        '--session_name', "GlassDock${Candidate}$timestamp",
        '--no_console_stats'
    )
    $capture = Start-Process -FilePath $presentMon -ArgumentList $arguments `
        -WorkingDirectory (Split-Path $presentMon) -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    if (-not $capture.WaitForExit(($DurationSeconds + 30) * 1000)) {
        Stop-Process -Id $capture.Id -Force
        throw 'PresentMon capture timed out.'
    }

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
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.Refresh()
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
        IntPtr found=IntPtr.Zero;
        EnumWindows((hwnd, _) => { GetWindowThreadProcessId(hwnd, out uint pid); if(pid==targetProcessId && IsWindowVisible(hwnd)){found=hwnd;return false;}return true;},IntPtr.Zero);
        return found;
    }
}
'@
        Add-Type -TypeDefinition $nativeMethods
        $window = [GlassDockGpuNativeMethods]::FindVisibleWindow($process.Id)
        if ($window -ne [IntPtr]::Zero) {
            [void][GlassDockGpuNativeMethods]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        }
        else { Stop-Process -Id $process.Id -Force }
    }
}
