param(
    [ValidateSet('Wpf', 'WinUI')]
    [string[]]$Candidates = @('Wpf', 'WinUI'),
    [ValidateSet('Bottom', 'Left', 'Right')]
    [string[]]$Edges = @('Bottom', 'Left', 'Right')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$nativeMethods = @'
using System;
using System.Runtime.InteropServices;
public struct GlassDockProbeRect { public int Left; public int Top; public int Right; public int Bottom; }
public struct GlassDockProbePoint { public int X; public int Y; }
public delegate bool GlassDockProbeEnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
public static class GlassDockProbeNativeMethods
{
    [DllImport("user32.dll")] public static extern bool EnumWindows(GlassDockProbeEnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out GlassDockProbeRect rect);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(GlassDockProbePoint point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder title, int maxCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    public static IntPtr FindVisibleWindow(int targetProcessId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == targetProcessId && IsWindowVisible(hwnd))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static bool IsPointOwnedByProcess(int x, int y, int targetProcessId)
    {
        IntPtr hwnd = WindowFromPoint(new GlassDockProbePoint { X = x, Y = y });
        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == targetProcessId;
    }
    public static string DescribePoint(int x, int y)
    {
        IntPtr hwnd = WindowFromPoint(new GlassDockProbePoint { X = x, Y = y });
        GetWindowThreadProcessId(hwnd, out uint processId);
        var className = new System.Text.StringBuilder(256);
        var title = new System.Text.StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);
        GetWindowText(hwnd, title, title.Capacity);
        return String.Format("hwnd={0};pid={1};class={2};title={3}", hwnd, processId, className, title);
    }
}
'@
Add-Type -TypeDefinition $nativeMethods

$stateRoot = Join-Path $script:LocalRoot 'poc-state'
$evidenceDirectory = Join-Path $script:LocalRoot 'evidence'
New-Item -ItemType Directory -Force -Path $stateRoot, $evidenceDirectory | Out-Null
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$csvPath = Join-Path $evidenceDirectory "window-tests-$timestamp.csv"
$results = [System.Collections.Generic.List[object]]::new()

foreach ($candidate in $Candidates) {
    $candidateState = Join-Path $stateRoot $candidate
    New-Item -ItemType Directory -Force -Path $candidateState | Out-Null
    $settingsFileName = if ($candidate -eq 'Wpf') { 'wpf-poc-settings.json' } else { 'winui-poc-settings.json' }
    $settingsPath = Join-Path $candidateState $settingsFileName
    $hadSettings = Test-Path -LiteralPath $settingsPath
    $backupPath = Join-Path $evidenceDirectory "settings-backup-$candidate-$timestamp.json"
    if ($hadSettings) { Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force }

    $projectFolder = if ($candidate -eq 'Wpf') {
        Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.Wpf'
    }
    else {
        Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.WinUI'
    }
    $framework = if ($candidate -eq 'Wpf') {
        'net10.0-windows10.0.19041.0'
    }
    else {
        'net10.0-windows10.0.26100.0\win-x64'
    }
    $exeName = if ($candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
    $exePath = Join-Path $projectFolder "bin\Release\$framework\$exeName"
    if (-not (Test-Path -LiteralPath $exePath)) { throw "Missing PoC executable: $exePath" }

    try {
        foreach ($edge in $Edges) {
            Set-Content -LiteralPath $settingsPath -Value "{`"Edge`":`"$edge`"}" -Encoding utf8
            $oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
            $oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
            $oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
            try {
                $env:GLASSDOCK_POC_ITEM_COUNT = '8'
                $env:GLASSDOCK_POC_ICON_SIZE = '58'
                $env:GLASSDOCK_POC_STATE_ROOT = $candidateState
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
                $window = [IntPtr]::Zero
                for ($attempt = 0; $attempt -lt 100; $attempt++) {
                    $process.Refresh()
                    if ($process.HasExited) { throw "$candidate exited before exposing its window (exit $($process.ExitCode))." }
                    $window = [GlassDockProbeNativeMethods]::FindVisibleWindow($process.Id)
                    if ($window -ne [IntPtr]::Zero) { break }
                    Start-Sleep -Milliseconds 100
                }
                if ($window -eq [IntPtr]::Zero) { throw "$candidate did not expose a visible top-level window." }
                Start-Sleep -Milliseconds 2500

                $rect = [GlassDockProbeRect]::new()
                if (-not [GlassDockProbeNativeMethods]::GetWindowRect($window, [ref]$rect)) {
                    throw "GetWindowRect failed for $candidate."
                }
                $centerX = [int](($rect.Left + $rect.Right) / 2)
                $centerY = [int](($rect.Top + $rect.Bottom) / 2)
                $centerHit = [GlassDockProbeNativeMethods]::IsPointOwnedByProcess($centerX, $centerY, $process.Id)
                $cornerHit = [GlassDockProbeNativeMethods]::IsPointOwnedByProcess($rect.Left + 2, $rect.Top + 2, $process.Id)
                $results.Add([pscustomobject]@{
                    Candidate = $candidate
                    Edge = $edge
                    ProcessId = $process.Id
                    WindowHandle = ('0x{0:X}' -f $window.ToInt64())
                    Dpi = [GlassDockProbeNativeMethods]::GetDpiForWindow($window)
                    Left = $rect.Left
                    Top = $rect.Top
                    Right = $rect.Right
                    Bottom = $rect.Bottom
                    ExtendedStyle = ('0x{0:X}' -f [GlassDockProbeNativeMethods]::GetWindowLongPtr($window, -20).ToInt64())
                    WindowIsTopmost = (([GlassDockProbeNativeMethods]::GetWindowLongPtr($window, -20).ToInt64() -band 0x00000008) -ne 0)
                    CenterHitsDock = $centerHit
                    TransparentCornerHitsDock = $cornerHit
                    AlphaHolePass = -not $cornerHit
                    CenterHit = [GlassDockProbeNativeMethods]::DescribePoint($centerX, $centerY)
                })
            }
            finally {
                if ($process -and -not $process.HasExited) {
                    Stop-Process -Id $process.Id -Force
                    $process.WaitForExit()
                }
            }
        }
    }
    finally {
        if ($hadSettings) { Copy-Item -LiteralPath $backupPath -Destination $settingsPath -Force }
        else { Remove-Item -LiteralPath $settingsPath -Force -ErrorAction SilentlyContinue }
    }
}

$results | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
$results | Format-Table -AutoSize
Write-Output "Evidence: $csvPath"
