[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$ConfirmInteractiveTest,
    [ValidateRange(10, 1800)]
    [int]$TimeoutSeconds = 900,
    [ValidateSet('Wpf', 'WinUI')]
    [string[]]$Candidates = @('Wpf', 'WinUI'),
    [ValidateSet('Bottom', 'Left', 'Right')]
    [string[]]$Edges = @('Bottom', 'Left', 'Right'),
    [ValidateSet('Light', 'Dark')]
    [string]$Theme = 'Light',
    [switch]$UseWinUiRoundedRegionCandidate
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

if ($UseWinUiRoundedRegionCandidate -and (@($Candidates | Where-Object { $_ -ne 'WinUI' }).Count -gt 0)) {
    throw '-UseWinUiRoundedRegionCandidate is restricted to a WinUI-only run.'
}

$nativeMethods = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

[StructLayout(LayoutKind.Sequential)]
public struct GlassDockM0Rect { public int Left; public int Top; public int Right; public int Bottom; }
[StructLayout(LayoutKind.Sequential)]
public struct GlassDockM0Point { public int X; public int Y; }
[StructLayout(LayoutKind.Sequential)]
public struct GlassDockM0MouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
[StructLayout(LayoutKind.Explicit)]
public struct GlassDockM0InputUnion { [FieldOffset(0)] public GlassDockM0MouseInput Mouse; }
[StructLayout(LayoutKind.Sequential)]
public struct GlassDockM0Input { public uint Type; public GlassDockM0InputUnion Data; }
public delegate bool GlassDockM0EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);

public static class GlassDockM0Native
{
    private const uint InputMouse = 0;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint VirtualDesktop = 0x4000;
    private const uint Absolute = 0x8000;
    private const ulong Marker = 0x474C415353444F43;

    [DllImport("user32.dll")] private static extern bool EnumWindows(GlassDockM0EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out GlassDockM0Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(GlassDockM0Point point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, GlassDockM0Input[] inputs, int size);

    public static IntPtr FindVisibleWindow(int targetPid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == targetPid && IsWindowVisible(hwnd)) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static bool TryGetRect(IntPtr hwnd, out GlassDockM0Rect rect)
    {
        return GetWindowRect(hwnd, out rect);
    }
    public static int GetWindowPidAt(int x, int y)
    {
        var hwnd = WindowFromPoint(new GlassDockM0Point { X = x, Y = y });
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        return unchecked((int)pid);
    }

    public static void MoveTo(int x, int y)
    {
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = Math.Max(1, GetSystemMetrics(78));
        var height = Math.Max(1, GetSystemMetrics(79));
        var normalizedX = (x - left) * 65535.0 / Math.Max(1, width - 1);
        var normalizedY = (y - top) * 65535.0 / Math.Max(1, height - 1);
        var dx = (int)Math.Round(Math.Max(0, Math.Min(65535, normalizedX)));
        var dy = (int)Math.Round(Math.Max(0, Math.Min(65535, normalizedY)));
        Send(MouseMove | Absolute | VirtualDesktop, dx, dy);
    }

    public static void LeftDown() { Send(MouseLeftDown, 0, 0); }
    public static void LeftUp() { Send(MouseLeftUp, 0, 0); }

    private static void Send(uint flags, int dx, int dy)
    {
        var input = new GlassDockM0Input
        {
            Type = InputMouse,
            Data = new GlassDockM0InputUnion
            {
                Mouse = new GlassDockM0MouseInput
                {
                    Dx = dx, Dy = dy, Flags = flags, ExtraInfo = new UIntPtr(Marker)
                }
            }
        };
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<GlassDockM0Input>());
        if (sent != 1) throw new InvalidOperationException(string.Format("SendInput failed; sent={0}; Win32={1}.", sent, Marshal.GetLastWin32Error()));
    }
}
'@
Add-Type -TypeDefinition $nativeMethods

$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$probeExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
if (-not (Test-Path -LiteralPath $probeExe)) { throw "Build the input probe first: $probeExe" }
$existingProbe = @(Get-Process -Name 'GlassDock.M0.InputProbe' -ErrorAction SilentlyContinue | Where-Object {
    try { [IO.Path]::GetFullPath($_.Path).Equals([IO.Path]::GetFullPath($probeExe), [StringComparison]::OrdinalIgnoreCase) }
    catch { $false }
})
if ($existingProbe.Count -gt 0) { throw 'An InputProbe process from this build already exists. It was left untouched; close it manually before scheduling this test.' }
Confirm-M0InteractiveTest -Confirmed:$ConfirmInteractiveTest -Purpose 'full-screen input sink with synthetic click and drag injection'
if (-not $PSCmdlet.ShouldProcess('Current desktop', 'Show a fullscreen test backdrop and inject synthetic mouse clicks and drags')) { return }
$session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
    -Purpose 'full-screen input routing: WPF and WinUI 3, selected edges, synthetic left click/double-click/drag' `
    -WatchdogExe $probeExe -TimeoutSeconds $TimeoutSeconds
$evidenceRoot = Join-Path $script:LocalRoot "evidence\m0-gap\input\$runId"
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$script:InteractiveSafetySession = $session
$runCompleted = $false

$results = [System.Collections.Generic.List[object]]::new()
$probeProcess = $null
$candidateProcess = $null
$oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
$oldTrace = $env:GLASSDOCK_M0_INPUT_TRACE
$oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
$oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
$oldWindowRegion = $env:GLASSDOCK_M0_TEST_ROUNDED_REGION

function Get-JsonLinesForScenario([string]$Path, [string]$Scenario) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    return @(Get-Content -LiteralPath $Path | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.Scenario -eq $Scenario })
}

function Set-Scenario([string]$Root, [string]$Scenario) {
    Set-Content -LiteralPath (Join-Path $Root 'current-scenario.txt') -Value $Scenario -NoNewline -Encoding utf8
}

function Set-DockScenario([string]$Root, [string]$Scenario) {
    Set-Content -LiteralPath (Join-Path $Root 'm0-current-scenario.txt') -Value $Scenario -NoNewline -Encoding utf8
}

function Invoke-MouseClick([int]$X, [int]$Y, [int]$Count = 1) {
    Assert-M0InteractiveTestCanContinue $script:InteractiveSafetySession
    Set-M0InteractiveCursorTarget $script:InteractiveSafetySession $X $Y
    [GlassDockM0Native]::MoveTo($X, $Y)
    Start-Sleep -Milliseconds 80
    for ($click = 0; $click -lt $Count; $click++) {
        Assert-M0InteractiveTestCanContinue $script:InteractiveSafetySession
        Set-M0InteractiveMouseButtonState $script:InteractiveSafetySession $true
        [GlassDockM0Native]::LeftDown()
        Start-Sleep -Milliseconds 45
        try { [GlassDockM0Native]::LeftUp() }
        finally { Set-M0InteractiveMouseButtonState $script:InteractiveSafetySession $false }
        if ($click + 1 -lt $Count) { Start-Sleep -Milliseconds 110 }
    }
}

function Invoke-MouseDrag([int]$FromX, [int]$FromY, [int]$ToX, [int]$ToY) {
    Assert-M0InteractiveTestCanContinue $script:InteractiveSafetySession
    Set-M0InteractiveCursorTarget $script:InteractiveSafetySession $FromX $FromY
    [GlassDockM0Native]::MoveTo($FromX, $FromY)
    Start-Sleep -Milliseconds 80
    Assert-M0InteractiveTestCanContinue $script:InteractiveSafetySession
    Set-M0InteractiveMouseButtonState $script:InteractiveSafetySession $true
    [GlassDockM0Native]::LeftDown()
    try {
        Start-Sleep -Milliseconds 80
        Assert-M0InteractiveTestCanContinue $script:InteractiveSafetySession
        $middleX=[int](($FromX + $ToX) / 2);$middleY=[int](($FromY + $ToY) / 2)
        Set-M0InteractiveCursorTarget $script:InteractiveSafetySession $middleX $middleY
        [GlassDockM0Native]::MoveTo($middleX,$middleY)
        Start-Sleep -Milliseconds 80
        Assert-M0InteractiveTestCanContinue $script:InteractiveSafetySession
        Set-M0InteractiveCursorTarget $script:InteractiveSafetySession $ToX $ToY
        [GlassDockM0Native]::MoveTo($ToX, $ToY)
        Start-Sleep -Milliseconds 80
    }
    finally {
        [GlassDockM0Native]::LeftUp()
        Set-M0InteractiveMouseButtonState $script:InteractiveSafetySession $false
    }
}

try {
    New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

    foreach ($candidate in $Candidates) {
        $projectFolder = Join-Path $script:RepoRoot "spikes\GlassDock.PoC.$candidate"
        $framework = if ($candidate -eq 'Wpf') { 'net10.0-windows10.0.19041.0' } else { 'net10.0-windows10.0.26100.0\win-x64' }
        $exeName = if ($candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
        $pocExe = Join-Path $projectFolder "bin\Release\$framework\$exeName"
        if (-not (Test-Path -LiteralPath $pocExe)) { throw "Build the solution first; missing PoC: $pocExe" }

        foreach ($edge in $Edges) {
            $candidateRoot = Join-Path $evidenceRoot "$candidate-$edge"
            $sinkRoot = Join-Path $candidateRoot 'sink'
            $stateRoot = Join-Path $candidateRoot 'state'
            New-Item -ItemType Directory -Force -Path $sinkRoot, $stateRoot | Out-Null
            $settingsName = if ($candidate -eq 'Wpf') { 'wpf-poc-settings.json' } else { 'winui-poc-settings.json' }
            Set-Content -LiteralPath (Join-Path $stateRoot $settingsName) -Value "{`"Edge`":`"$edge`"}" -Encoding utf8
            $readyPath = Join-Path $sinkRoot 'input-probe-ready.json'
            $eventPath = Join-Path $sinkRoot 'lower-window-events.jsonl'
            $dockTracePath = Join-Path $stateRoot 'm0-input-events.jsonl'
            $dockLogPath = Join-Path $stateRoot 'logs\dock.log'

            Assert-M0InteractiveTestCanContinue $session
            $probeProcess = Start-M0OwnedTestProcess -Session $session -FilePath $probeExe -ArgumentList @('--theme', $Theme, '--output-root', $sinkRoot)
            for ($attempt = 0; $attempt -lt 80 -and -not (Test-Path -LiteralPath $readyPath); $attempt++) { Start-Sleep -Milliseconds 100 }
            if (-not (Test-Path -LiteralPath $readyPath)) { throw "Input probe did not become ready: $candidate/$edge" }
            $ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
            if (-not $ready.ForegroundConfirmed -or -not $ready.ExpectedAreaCovered) { throw "Input probe refused test: $candidate/$edge" }

            $env:GLASSDOCK_POC_STATE_ROOT = $stateRoot
            $env:GLASSDOCK_M0_INPUT_TRACE = '1'
            $env:GLASSDOCK_POC_ITEM_COUNT = '8'
            $env:GLASSDOCK_POC_ICON_SIZE = '58'
            if ($UseWinUiRoundedRegionCandidate) { $env:GLASSDOCK_M0_TEST_ROUNDED_REGION = '1' }
            else { Remove-Item Env:GLASSDOCK_M0_TEST_ROUNDED_REGION -ErrorAction SilentlyContinue }
            $candidateProcess = Start-M0OwnedTestProcess -Session $session -FilePath $pocExe -WorkingDirectory $projectFolder
            for ($attempt = 0; $attempt -lt 100; $attempt++) {
                $candidateProcess.Refresh()
                if ($candidateProcess.HasExited) { throw "$candidate exited early for $edge (exit $($candidateProcess.ExitCode))." }
                $dockHwnd = [GlassDockM0Native]::FindVisibleWindow($candidateProcess.Id)
                if ($dockHwnd -ne [IntPtr]::Zero) { break }
                Start-Sleep -Milliseconds 100
            }
            if ($dockHwnd -eq [IntPtr]::Zero) { throw "$candidate did not expose a visible window for $edge." }
            Start-Sleep -Milliseconds 900
            $rect = [GlassDockM0Rect]::new()
            if (-not [GlassDockM0Native]::TryGetRect($dockHwnd, [ref]$rect)) { throw "GetWindowRect failed for $candidate/$edge." }
            $width = $rect.Right - $rect.Left
            $height = $rect.Bottom - $rect.Top
            $cornerX = $rect.Left + 2
            $cornerY = $rect.Top + 2
            if ($edge -eq 'Bottom') { $iconX = $rect.Left + [int]($width * 0.30); $iconY = $rect.Top + [int]($height * 0.35) }
            else { $iconX = $rect.Left + [int]($width * 0.50); $iconY = $rect.Top + [int]($height * 0.24) }

            $probePositions = if ($edge -eq 'Bottom') {
                @(@(0.22,0.35),@(0.30,0.35),@(0.38,0.35),@(0.46,0.35),@(0.54,0.35),@(0.62,0.35),@(0.70,0.35),@(0.78,0.35))
            } else {
                @(@(0.50,0.18),@(0.50,0.26),@(0.50,0.34),@(0.50,0.42),@(0.50,0.50),@(0.50,0.58),@(0.50,0.66),@(0.50,0.74))
            }
            $iconX = -1; $iconY = -1; $selectionScenario = 0
            foreach ($fraction in $probePositions) {
                $tryX = $rect.Left + [int]($width * $fraction[0])
                $tryY = $rect.Top + [int]($height * $fraction[1])
                $selectionScenario++
                $scenario = "locate-icon-$selectionScenario"
                Set-Scenario $sinkRoot $scenario
                Set-DockScenario $stateRoot $scenario
                Invoke-MouseClick $tryX $tryY
                Start-Sleep -Milliseconds 140
                $clicked = (Test-Path -LiteralPath $dockLogPath) -and ((Select-String -LiteralPath $dockLogPath -Pattern "M0 scenario=$scenario" -SimpleMatch -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0)
                if ($clicked) { $iconX = $tryX; $iconY = $tryY; break }
            }

            $tests = @()
            if ($iconX -ge 0) {
                $tests += [pscustomobject]@{ Name='dock-single'; Action={ Invoke-MouseClick $iconX $iconY }; Expected='Dock receives input; lower window does not' }
                $tests += [pscustomobject]@{ Name='dock-double'; Action={ Invoke-MouseClick $iconX $iconY 2 }; Expected='Dock receives two clicks; lower window does not' }
                $tests += [pscustomobject]@{ Name='drag-dock-to-corner'; Action={ Invoke-MouseDrag $iconX $iconY $cornerX $cornerY }; Expected='Dock retains pointer capture for drag' }
            }
            $tests += [pscustomobject]@{ Name='transparent-single'; Action={ Invoke-MouseClick $cornerX $cornerY }; Expected='Lower window receives click' }
            $tests += [pscustomobject]@{ Name='transparent-double'; Action={ Invoke-MouseClick $cornerX $cornerY 2 }; Expected='Lower window receives double click sequence' }
            if ($iconX -ge 0) {
                $tests += [pscustomobject]@{ Name='drag-corner-to-dock'; Action={ Invoke-MouseDrag $cornerX $cornerY $iconX $iconY }; Expected='Initial transparent down remains with lower window' }
            }

            foreach ($test in $tests) {
                Assert-M0InteractiveTestCanContinue $session
                $scenario = $test.Name
                Set-Scenario $sinkRoot $scenario
                Set-DockScenario $stateRoot $scenario
                $cornerOwner = [GlassDockM0Native]::GetWindowPidAt($cornerX, $cornerY)
                $iconOwner = if ($iconX -ge 0) { [GlassDockM0Native]::GetWindowPidAt($iconX, $iconY) } else { -1 }
                & $test.Action
                Start-Sleep -Milliseconds 220
                $lowerRows = @(Get-JsonLinesForScenario $eventPath $scenario)
                $dockRows = @(Get-JsonLinesForScenario $dockTracePath $scenario)
                $dockClicks = if (Test-Path -LiteralPath $dockLogPath) { @(Select-String -LiteralPath $dockLogPath -Pattern "M0 scenario=$scenario" -SimpleMatch | Where-Object { $_.Line -match 'Placeholder selected:' }).Count } else { 0 }
                $lowerClicks = @($lowerRows | Where-Object { $_.Event -in @('WM_LBUTTONDOWN','WM_LBUTTONDBLCLK') }).Count
                $lowerPointerMoves = @($lowerRows | Where-Object { $_.Event -eq 'WM_MOUSEMOVE_LEFT_HELD' }).Count
                $dockPointerEvents = $dockRows.Count
                $expectedPass = switch ($scenario) {
                    'dock-single' { $dockClicks -ge 1 -and $lowerClicks -eq 0 }
                    'dock-double' { $dockClicks -ge 2 -and $lowerClicks -eq 0 }
                    'transparent-single' { $lowerClicks -ge 1 -and $dockPointerEvents -eq 0 }
                    'transparent-double' { $lowerClicks -ge 2 -and $dockPointerEvents -eq 0 }
                    'drag-dock-to-corner' { $dockPointerEvents -ge 2 -and $lowerClicks -eq 0 }
                    'drag-corner-to-dock' { $lowerClicks -ge 1 -or $lowerPointerMoves -gt 0 }
                    default { $false }
                }
                $results.Add([pscustomobject]@{
                    Candidate = $candidate
                    CandidateVariant = if ($UseWinUiRoundedRegionCandidate) { 'WinUI SetWindowRgn rounded-region opt-in' } else { 'Default' }
                    Edge = $edge
                    Theme = $Theme
                    Scenario = $scenario
                    IconPoint = if ($iconX -ge 0) { "$iconX,$iconY" } else { 'not-found' }
                    TransparentPoint = "$cornerX,$cornerY"
                    WindowFromPointAtIconPid = $iconOwner
                    WindowFromPointAtCornerPid = $cornerOwner
                    DockPointerEvents = $dockPointerEvents
                    DockClickActions = $dockClicks
                    LowerWindowClickMessages = $lowerClicks
                    LowerWindowDragMoves = $lowerPointerMoves
                    ActualReceiver = if ($dockPointerEvents -gt 0 -or $dockClicks -gt 0) { if ($lowerClicks -gt 0) { 'both/ambiguous' } else { 'Dock' } } elseif ($lowerClicks -gt 0 -or $lowerPointerMoves -gt 0) { 'lower-window' } else { 'neither-observed' }
                    Status = if ($expectedPass) { 'PASS' } else { 'FAIL' }
                    Expected = $test.Expected
                })
            }

            if ($candidateProcess) { [void](Stop-M0OwnedTestProcess $session $candidateProcess) }
            $candidateProcess = $null
            if ($probeProcess) { [void](Stop-M0OwnedTestProcess $session $probeProcess) }
            $probeProcess = $null
            $env:GLASSDOCK_POC_STATE_ROOT = $oldStateRoot
        }
    }
    $runCompleted = $true
}
finally {
    foreach ($ownedProcess in @($candidateProcess, $probeProcess)) {
        if ($ownedProcess) { try { [void](Stop-M0OwnedTestProcess $session $ownedProcess) } catch { Write-Warning "Owned-process cleanup failed: $($_.Exception.Message)" } }
    }
    foreach ($entry in @(
        @{ Name='GLASSDOCK_POC_STATE_ROOT'; Value=$oldStateRoot },
        @{ Name='GLASSDOCK_M0_INPUT_TRACE'; Value=$oldTrace },
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

$csvPath = Join-Path $evidenceRoot 'actual-input-results.csv'
$results | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
$results | Format-Table Candidate, Edge, Scenario, ActualReceiver, DockPointerEvents, DockClickActions, LowerWindowClickMessages, Status -AutoSize
Write-Output "Evidence: $csvPath"
