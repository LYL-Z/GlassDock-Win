if (-not ('GlassDockM0SafetyNative' -as [type])) {
    $safetyNativeSource = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

[UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate bool GlassDockEnumWindowsCallback(IntPtr hwnd, IntPtr lParam);

[StructLayout(LayoutKind.Sequential)] public struct GlassDockM0SafetyRect { public int Left; public int Top; public int Right; public int Bottom; }
[StructLayout(LayoutKind.Sequential)] public struct GlassDockM0SafetyPoint { public int X; public int Y; }
[StructLayout(LayoutKind.Sequential)] public struct GlassDockM0SafetyMouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
[StructLayout(LayoutKind.Explicit)] public struct GlassDockM0SafetyInputUnion { [FieldOffset(0)] public GlassDockM0SafetyMouseInput Mouse; }
[StructLayout(LayoutKind.Sequential)] public struct GlassDockM0SafetyInput { public uint Type; public GlassDockM0SafetyInputUnion Data; }

public static class GlassDockM0SafetyNative
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd, out GlassDockM0SafetyRect rect);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool GetCursorPos(out GlassDockM0SafetyPoint point);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool EnumWindows(GlassDockEnumWindowsCallback callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint count, GlassDockM0SafetyInput[] inputs, int size);

    public static string GetTitle(IntPtr hwnd)
    {
        var text = new StringBuilder(512);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    public static IntPtr FindFirstVisibleWindowForProcess(uint targetProcessId)
    {
        IntPtr found = IntPtr.Zero;
        GlassDockEnumWindowsCallback callback = (hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            uint processId = 0;
            GetWindowThreadProcessId(hwnd, out processId);
            if (processId != targetProcessId) return true;
            found = hwnd;
            return false;
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }
}
'@
    Add-Type -TypeDefinition $safetyNativeSource
}

$script:M0InteractiveTestEnvironmentNames = @(
    'GLASSDOCK_POC_STATE_ROOT',
    'GLASSDOCK_M0_INPUT_TRACE',
    'GLASSDOCK_M0_TEST_ALLOW_HIDE',
    'GLASSDOCK_POC_ITEM_COUNT',
    'GLASSDOCK_POC_ICON_SIZE',
    'GLASSDOCK_M0_TEST_ROUNDED_REGION',
    'GLASSDOCK_M0_VISUAL_CANDIDATE',
    'GLASSDOCK_M0_COMPOSITION_PROBE_MODE',
    'GLASSDOCK_M0_COMPOSITION_STATUS_ROOT',
    'GLASSDOCK_M0_NONACTIVATING_START'
)

function Confirm-M0InteractiveTest {
    param(
        [Parameter(Mandatory)][switch]$Confirmed,
        [Parameter(Mandatory)][string]$Purpose,
        [string]$AuthorizationPhrase,
        [string]$RequiredPhrase = 'RUN M0 DESKTOP TEST'
    )

    if (-not $Confirmed) {
        throw "Interactive test was not started. Re-run with -ConfirmInteractiveTest, then type the exact confirmation phrase when prompted. Purpose: $Purpose"
    }
    if ($PSBoundParameters.ContainsKey('AuthorizationPhrase')) {
        if ($AuthorizationPhrase -cne $RequiredPhrase) {
            throw 'Interactive test was not started because the exact confirmation phrase was not supplied.'
        }
        return
    }
    $answer = Read-Host "Desktop-impacting M0 test: $Purpose. Emergency stop: Ctrl+Shift+F12. Type $RequiredPhrase to continue"
    if ($answer -cne $RequiredPhrase) {
        throw 'Interactive test was not started because the exact confirmation phrase was not entered.'
    }
}

function Get-M0InteractiveDesktopSnapshot {
    # Keep pre/post rectangles and SendInput coordinates in the same physical-pixel space.
    # UI Automation can otherwise change the caller thread's DPI virtualization context.
    $previousDpiContext = [GlassDockM0SafetyNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
    $dpiContextSwitchSucceeded = $previousDpiContext -ne [IntPtr]::Zero
    $foreground = [GlassDockM0SafetyNative]::GetForegroundWindow()
    [uint32]$foregroundPid = 0
    if ($foreground -ne [IntPtr]::Zero) { [void][GlassDockM0SafetyNative]::GetWindowThreadProcessId($foreground, [ref]$foregroundPid) }
    $foregroundRect = [GlassDockM0SafetyRect]::new()
    $foregroundHasRect = $foreground -ne [IntPtr]::Zero -and [GlassDockM0SafetyNative]::GetWindowRect($foreground, [ref]$foregroundRect)
    $taskbar = [GlassDockM0SafetyNative]::FindWindow('Shell_TrayWnd', $null)
    $taskbarRect = [GlassDockM0SafetyRect]::new()
    $taskbarHasRect = $taskbar -ne [IntPtr]::Zero -and [GlassDockM0SafetyNative]::GetWindowRect($taskbar, [ref]$taskbarRect)
    $cursor = [GlassDockM0SafetyPoint]::new()
    $cursorAvailable = [GlassDockM0SafetyNative]::GetCursorPos([ref]$cursor)
    $bounds = [ordered]@{
        PrimaryWidth = [GlassDockM0SafetyNative]::GetSystemMetrics(0)
        PrimaryHeight = [GlassDockM0SafetyNative]::GetSystemMetrics(1)
        VirtualLeft = [GlassDockM0SafetyNative]::GetSystemMetrics(76)
        VirtualTop = [GlassDockM0SafetyNative]::GetSystemMetrics(77)
        VirtualWidth = [GlassDockM0SafetyNative]::GetSystemMetrics(78)
        VirtualHeight = [GlassDockM0SafetyNative]::GetSystemMetrics(79)
    }
    return [ordered]@{
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Foreground = [ordered]@{
            Hwnd = $foreground.ToInt64()
            ProcessId = $foregroundPid
            Title = if ($foreground -ne [IntPtr]::Zero) { [GlassDockM0SafetyNative]::GetTitle($foreground) } else { '' }
            Bounds = if ($foregroundHasRect) { [ordered]@{ Left=$foregroundRect.Left; Top=$foregroundRect.Top; Right=$foregroundRect.Right; Bottom=$foregroundRect.Bottom } } else { $null }
        }
        Taskbar = [ordered]@{
            Hwnd = $taskbar.ToInt64()
            Visible = $taskbar -ne [IntPtr]::Zero -and [GlassDockM0SafetyNative]::IsWindowVisible($taskbar)
            Bounds = if ($taskbarHasRect) { [ordered]@{ Left=$taskbarRect.Left; Top=$taskbarRect.Top; Right=$taskbarRect.Right; Bottom=$taskbarRect.Bottom } } else { $null }
        }
        Cursor = [ordered]@{ Available=$cursorAvailable; X=$cursor.X; Y=$cursor.Y }
        ScreenBounds = $bounds
        CoordinateSpace = if ($dpiContextSwitchSucceeded) { 'PhysicalPixels; thread DPI context is Per-Monitor-V2' } else { 'Caller DPI context unchanged; values may be virtualized' }
    }
}

function Write-M0SafetyManifest {
    param([Parameter(Mandatory)]$Session)

    $tempPath = "$($Session.ManifestPath).tmp"
    $json = $Session.Manifest | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($tempPath, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tempPath -Destination $Session.ManifestPath -Force
}

function ConvertTo-M0ProcessIdentity {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)

    $processId = $Process.Id
    $executablePath = ''
    $startTimeUtcTicks = 0L
    for ($attempt = 0; $attempt -lt 10 -and [string]::IsNullOrWhiteSpace($executablePath); $attempt++) {
        try {
            $liveProcess = Get-Process -Id $processId -ErrorAction Stop
            $liveProcess.Refresh()
            $executablePath = [string]$liveProcess.Path
            if ([string]::IsNullOrWhiteSpace($executablePath)) {
                $processInfo = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$processId" -ErrorAction Stop
                if ($processInfo) { $executablePath = [string]$processInfo.ExecutablePath }
            }
            if (-not [string]::IsNullOrWhiteSpace($executablePath)) {
                $startTimeUtcTicks = $liveProcess.StartTime.ToUniversalTime().Ticks
                break
            }
        }
        catch { $executablePath = '' }
        if ([string]::IsNullOrWhiteSpace($executablePath)) { Start-Sleep -Milliseconds 100 }
    }
    if ([string]::IsNullOrWhiteSpace($executablePath) -or $startTimeUtcTicks -le 0) {
        throw "Could not verify the executable path and start time for owned process PID=$processId."
    }
    return [ordered]@{
        ProcessId = $processId
        ExecutablePath = [IO.Path]::GetFullPath($executablePath)
        StartTimeUtcTicks = $startTimeUtcTicks
    }
}

function Test-M0ProcessIdentity {
    param([Parameter(Mandatory)]$Identity, [Parameter(Mandatory)][string]$RepoRoot)

    try {
        $process = Get-Process -Id ([int]$Identity.ProcessId) -ErrorAction Stop
        $process.Refresh()
        $actualPath = [IO.Path]::GetFullPath($process.Path)
        $expectedPath = [IO.Path]::GetFullPath([string]$Identity.ExecutablePath)
        $insideRepo = $expectedPath.StartsWith(([IO.Path]::GetFullPath($RepoRoot).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)
        return $insideRepo -and
            $actualPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase) -and
            $process.StartTime.ToUniversalTime().Ticks -eq [long]$Identity.StartTimeUtcTicks
    }
    catch { return $false }
}

function Stop-M0OwnedProcess {
    param([Parameter(Mandatory)]$Identity, [Parameter(Mandatory)][string]$RepoRoot, [int]$GraceMilliseconds = 3000)

    $process = Get-Process -Id ([int]$Identity.ProcessId) -ErrorAction SilentlyContinue
    if (-not $process) { return [pscustomobject]@{ ProcessId=$Identity.ProcessId; Stopped=$true; Method='already-exited'; Refused=$false } }
    if (-not (Test-M0ProcessIdentity $Identity $RepoRoot)) { return [pscustomobject]@{ ProcessId=$Identity.ProcessId; Stopped=$false; Method='identity-mismatch'; Refused=$true } }
    $method = 'WM_CLOSE'
    if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
        [void][GlassDockM0SafetyNative]::PostMessage($process.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
        if ($process.WaitForExit($GraceMilliseconds)) { return [pscustomobject]@{ ProcessId=$Identity.ProcessId; Stopped=$true; Method=$method; Refused=$false } }
    }
    else {
        $method = 'identity-checked Stop-Process'
    }
    if (Test-M0ProcessIdentity $Identity $RepoRoot) {
        Stop-Process -Id ([int]$Identity.ProcessId) -Force -ErrorAction Stop
        try { (Get-Process -Id ([int]$Identity.ProcessId) -ErrorAction Stop).WaitForExit(5000) } catch { }
    }
    return [pscustomobject]@{ ProcessId=$Identity.ProcessId; Stopped=(-not (Test-M0ProcessIdentity $Identity $RepoRoot)); Method=$method; Refused=$false }
}

function Start-M0InteractiveTestSession {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$LocalRoot,
        [Parameter(Mandatory)][string]$Purpose,
        [Parameter(Mandatory)][string]$WatchdogExe,
        [ValidateRange(10, 1800)][int]$TimeoutSeconds = 900
    )

    $runId = [Guid]::NewGuid().ToString('N')
    $evidenceRoot = Join-Path $LocalRoot "evidence\interactive-tests\$runId"
    New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
    $preState = Get-M0InteractiveDesktopSnapshot
    $preState.Purpose = $Purpose
    $preState.RunId = $runId
    $preState.ProcessId = $PID
    $preStatePath = Join-Path $evidenceRoot 'pre-state.json'
    [IO.File]::WriteAllText($preStatePath, ($preState | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))

    $environment = [ordered]@{}
    foreach ($name in $script:M0InteractiveTestEnvironmentNames) { $environment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    $session = [pscustomobject]@{
        RunId = $runId
        Purpose = $Purpose
        RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
        LocalRoot = [IO.Path]::GetFullPath($LocalRoot)
        EvidenceRoot = $evidenceRoot
        PreStatePath = $preStatePath
        StopPath = Join-Path $evidenceRoot 'stop-signal.json'
        ReadyPath = Join-Path $evidenceRoot 'watchdog-ready.json'
        WatchdogLogPath = Join-Path $evidenceRoot 'watchdog.jsonl'
        ManifestPath = Join-Path $evidenceRoot 'run-manifest.json'
        PreviousDesktopState = $preState
        EnvironmentBefore = $environment
        RestorationChecks = [System.Collections.Generic.List[object]]::new()
        ProcessIdentities = [System.Collections.Generic.List[object]]::new()
        WatchdogProcess = $null
        WatchdogIdentity = $null
        Completed = $false
        MouseButtonDown = $false
        RightMouseButtonDown = $false
        Manifest = $null
    }
    $session.Manifest = [ordered]@{
        RunId = $runId
        AllowedRoot = $session.LocalRoot
        RepoRoot = $session.RepoRoot
        EvidenceRoot = $session.EvidenceRoot
        DeadlineUnixMilliseconds = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds).ToUnixTimeMilliseconds()
        ForegroundWindowHandle = [long]$preState.Foreground.Hwnd
        ForegroundProcessId = [int]$preState.Foreground.ProcessId
        OriginalCursorX = [int]$preState.Cursor.X
        OriginalCursorY = [int]$preState.Cursor.Y
        InjectedCursorX = [int]$preState.Cursor.X
        InjectedCursorY = [int]$preState.Cursor.Y
        CursorMovePending = $false
        MouseButtonDown = $false
        RightMouseButtonDown = $false
        Processes = @()
        FileRestores = @()
    }
    Write-M0SafetyManifest $session

    $WatchdogExe = [IO.Path]::GetFullPath($WatchdogExe)
    if (-not $WatchdogExe.StartsWith(($session.RepoRoot.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Watchdog executable is outside the repository: $WatchdogExe"
    }
    if (-not (Test-Path -LiteralPath $WatchdogExe)) { throw "Safety watchdog executable missing: $WatchdogExe" }
    $args = @('--watchdog', '--manifest', $session.ManifestPath, '--stop-path', $session.StopPath,
        '--ready-path', $session.ReadyPath, '--log-path', $session.WatchdogLogPath,
        '--timeout-seconds', [string]$TimeoutSeconds, '--run-id', $runId)
    $quotedArgs = $args | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }
    $session.WatchdogProcess = Start-Process -FilePath $WatchdogExe -ArgumentList ($quotedArgs -join ' ') -WorkingDirectory (Split-Path -Parent $WatchdogExe) -PassThru -WindowStyle Hidden
    try {
        $session.WatchdogIdentity = ConvertTo-M0ProcessIdentity $session.WatchdogProcess
        for ($attempt = 0; $attempt -lt 100 -and -not (Test-Path -LiteralPath $session.ReadyPath); $attempt++) {
            $session.WatchdogProcess.Refresh()
            if ($session.WatchdogProcess.HasExited) { throw "Safety watchdog exited during startup (code $($session.WatchdogProcess.ExitCode))." }
            Start-Sleep -Milliseconds 100
        }
        if (-not (Test-Path -LiteralPath $session.ReadyPath)) { throw 'Safety watchdog did not become ready within 10 seconds.' }
        $ready = Get-Content -LiteralPath $session.ReadyPath -Raw | ConvertFrom-Json
        if (-not $ready.Success -or $ready.RunId -ne $runId) { throw "Safety watchdog refused the run: $($ready.Error)" }
        return $session
    }
    catch {
        if ($session.WatchdogIdentity) { [void](Stop-M0OwnedProcess $session.WatchdogIdentity $session.RepoRoot) }
        elseif ($session.WatchdogProcess -and -not $session.WatchdogProcess.HasExited) {
            $stopSignal = @{ RunId=$runId; Reason='runner-initialization-failed'; TimestampUtc=[DateTimeOffset]::UtcNow.ToString('O') }
            [IO.File]::WriteAllText($session.StopPath, ($stopSignal | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
            [void]$session.WatchdogProcess.WaitForExit(5000)
        }
        throw
    }
}

function Start-M0OwnedTestProcess {
    param(
        [Parameter(Mandatory)]$Session,
        [Parameter(Mandatory)][string]$FilePath,
        [AllowEmptyCollection()][string[]]$ArgumentList = @(),
        [string]$WorkingDirectory
    )

    Assert-M0InteractiveTestCanContinue $Session
    $fullPath = [IO.Path]::GetFullPath($FilePath)
    if (-not $fullPath.StartsWith(($Session.RepoRoot.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to launch a test executable outside this repository: $fullPath"
    }
    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) { $WorkingDirectory = Split-Path -Parent $fullPath }
    if ($ArgumentList.Count -gt 0) {
        $process = Start-Process -FilePath $fullPath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -PassThru
    }
    else {
        $process = Start-Process -FilePath $fullPath -WorkingDirectory $WorkingDirectory -PassThru
    }
    try {
        Register-M0OwnedInteractiveProcess -Session $Session -Process $process | Out-Null
        return $process
    }
    catch {
        # Start-Process returned this exact process object, so cleanup is scoped to the process started by this invocation.
        try { if (-not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(5000) } } catch { }
        throw
    }
}

function Register-M0OwnedInteractiveProcess {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][Diagnostics.Process]$Process)

    $identity = ConvertTo-M0ProcessIdentity $Process
    $Session.ProcessIdentities.Add($identity)
    $Session.Manifest.Processes = @($Session.ProcessIdentities)
    Write-M0SafetyManifest $Session
    return $identity
}

function Stop-M0OwnedTestProcess {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][Diagnostics.Process]$Process)

    $identity = @($Session.ProcessIdentities | Where-Object { [int]$_.ProcessId -eq $Process.Id } | Select-Object -Last 1)
    if ($identity.Count -eq 0) { throw "Refusing to stop PID $($Process.Id): it is not registered to this run." }
    $result = Stop-M0OwnedProcess $identity[0] $Session.RepoRoot
    if ($result.Stopped) {
        $remaining = @($Session.ProcessIdentities | Where-Object { [int]$_.ProcessId -ne $Process.Id })
        $Session.ProcessIdentities.Clear()
        foreach ($entry in $remaining) { $Session.ProcessIdentities.Add($entry) }
        $Session.Manifest.Processes = @($Session.ProcessIdentities)
        Write-M0SafetyManifest $Session
    }
    return $result
}

function Register-M0InteractiveRestoreFile {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][string]$TargetPath)

    $target = [IO.Path]::GetFullPath($TargetPath)
    if (-not $target.StartsWith(($Session.LocalRoot.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Interactive test may only register restore targets inside .local: $target"
    }
    $existed = Test-Path -LiteralPath $target -PathType Leaf
    $backup = if ($existed) { Join-Path $Session.EvidenceRoot ("restore-" + [Guid]::NewGuid().ToString('N') + '.bak') } else { $null }
    if ($existed) { Copy-Item -LiteralPath $target -Destination $backup -Force }
    $entries = @($Session.Manifest.FileRestores) + @([ordered]@{ TargetPath=$target; BackupPath=$backup; TargetExisted=$existed })
    $Session.Manifest.FileRestores = $entries
    Write-M0SafetyManifest $Session
}

function Set-M0InteractiveMouseButtonState {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][bool]$IsDown)
    $Session.MouseButtonDown = $IsDown
    $Session.Manifest.MouseButtonDown = $IsDown
    Write-M0SafetyManifest $Session
}

function Set-M0InteractiveRightMouseButtonState {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][bool]$IsDown)
    $Session.RightMouseButtonDown = $IsDown
    $Session.Manifest.RightMouseButtonDown = $IsDown
    Write-M0SafetyManifest $Session
}

function Set-M0InteractiveCursorTarget {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][int]$X, [Parameter(Mandatory)][int]$Y)
    $Session.Manifest.InjectedCursorX = $X
    $Session.Manifest.InjectedCursorY = $Y
    $Session.Manifest.CursorMovePending = $true
    Write-M0SafetyManifest $Session
}

function Assert-M0InteractiveTestCanContinue {
    param([Parameter(Mandatory)]$Session)

    if (Test-Path -LiteralPath $Session.StopPath) {
        $signal = Get-Content -LiteralPath $Session.StopPath -Raw | ConvertFrom-Json
        throw "Interactive test stopped by safety controller: $($signal.Reason)."
    }
    $Session.WatchdogProcess.Refresh()
    if ($Session.WatchdogProcess.HasExited) { throw 'Safety watchdog exited unexpectedly; test work is stopped.' }
}

function Add-M0InteractiveRestorationCheck {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][bool]$Passed, [string]$Details = '')
    $Session.RestorationChecks.Add([pscustomobject]@{ Name=$Name; Passed=$Passed; Details=$Details })
}

function Restore-M0InteractiveTestEnvironment {
    param([Parameter(Mandatory)]$Session)

    foreach ($name in $script:M0InteractiveTestEnvironmentNames) {
        $value = $Session.EnvironmentBefore[$name]
        if ($null -eq $value) {
            Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable($name, [string]$value, 'Process')
        }
    }
}

function Stop-M0InteractiveTestSession {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][bool]$Completed)

    if ($Session.MouseButtonDown) {
        $input = [GlassDockM0SafetyInput]::new()
        $input.Type = 0
        $input.Data = [GlassDockM0SafetyInputUnion]::new()
        $input.Data.Mouse = [GlassDockM0SafetyMouseInput]::new()
        $input.Data.Mouse.Flags = 0x0004
        $input.Data.Mouse.ExtraInfo = [UIntPtr]0x474C415353444F43
        $sent = [GlassDockM0SafetyNative]::SendInput(1, @($input), [Runtime.InteropServices.Marshal]::SizeOf([type][GlassDockM0SafetyInput]))
        Add-M0InteractiveRestorationCheck $Session 'Injected left button released' ($sent -eq 1) "SendInput release count=$sent"
        Set-M0InteractiveMouseButtonState $Session $false
    }

    if ($Session.RightMouseButtonDown) {
        $input = [GlassDockM0SafetyInput]::new()
        $input.Type = 0
        $input.Data = [GlassDockM0SafetyInputUnion]::new()
        $input.Data.Mouse = [GlassDockM0SafetyMouseInput]::new()
        $input.Data.Mouse.Flags = 0x0010
        $input.Data.Mouse.ExtraInfo = [UIntPtr]0x474C415353444F43
        $sent = [GlassDockM0SafetyNative]::SendInput(1, @($input), [Runtime.InteropServices.Marshal]::SizeOf([type][GlassDockM0SafetyInput]))
        Add-M0InteractiveRestorationCheck $Session 'Injected right button released' ($sent -eq 1) "SendInput release count=$sent"
        Set-M0InteractiveRightMouseButtonState $Session $false
    }

    $stopResults = @()
    foreach ($identity in @($Session.ProcessIdentities)) {
        $stopResults += Stop-M0OwnedProcess $identity $Session.RepoRoot
    }
    $processesStopped = @($stopResults | Where-Object { -not $_.Stopped }).Count -eq 0
    Add-M0InteractiveRestorationCheck $Session 'Only run-owned processes stopped' $processesStopped (($stopResults | ConvertTo-Json -Compress -Depth 5) -join '')

    foreach ($entry in @($Session.Manifest.FileRestores)) {
        $target = [IO.Path]::GetFullPath($entry.TargetPath)
        try {
            if ($entry.TargetExisted) { Copy-Item -LiteralPath $entry.BackupPath -Destination $target -Force }
            elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
            $restored = if ($entry.TargetExisted) { Test-Path -LiteralPath $target -PathType Leaf } else { -not (Test-Path -LiteralPath $target) }
            Add-M0InteractiveRestorationCheck $Session "File restored: $target" $restored "Previously existed=$($entry.TargetExisted)"
        }
        catch {
            Add-M0InteractiveRestorationCheck $Session "File restored: $target" $false $_.Exception.Message
        }
    }

    foreach ($name in $script:M0InteractiveTestEnvironmentNames) {
        $expected = $Session.EnvironmentBefore[$name]
        $actual = [Environment]::GetEnvironmentVariable($name, 'Process')
        $passed = if ($null -eq $expected) { $null -eq $actual } else { $actual -ceq [string]$expected }
        Add-M0InteractiveRestorationCheck $Session "Environment restored: $name" $passed `
            "Expected=[$expected]; actual=[$actual]; expectedWasUnset=$($null -eq $expected); actualIsUnset=$($null -eq $actual)"
    }

    $originalHwnd = [IntPtr][long]$Session.PreviousDesktopState.Foreground.Hwnd
    $originalProcessId = [uint32]$Session.PreviousDesktopState.Foreground.ProcessId
    $focusRestore = $true
    $focusDetails = 'No foreground HWND was captured.'
    if ($originalHwnd -ne [IntPtr]::Zero) {
        $actualProcessId = [uint32]0
        $validWindow = [GlassDockM0SafetyNative]::IsWindow($originalHwnd)
        if ($validWindow) { [void][GlassDockM0SafetyNative]::GetWindowThreadProcessId($originalHwnd,[ref]$actualProcessId) }
        $validWindow = $validWindow -and ($originalProcessId -eq 0 -or $actualProcessId -eq $originalProcessId)
        $focusRestore = $validWindow
        $focusDetails = "Original hwnd=0x{0:X}; expected pid={1}; actual pid={2}; valid={3}" -f $originalHwnd.ToInt64(),$originalProcessId,$actualProcessId,$validWindow
        if ($validWindow) {
            for ($attempt=1; $attempt -le 12; $attempt++) {
                [void][GlassDockM0SafetyNative]::SetForegroundWindow($originalHwnd)
                Start-Sleep -Milliseconds 150
                if ([GlassDockM0SafetyNative]::GetForegroundWindow() -eq $originalHwnd) { break }
            }
            $focusRestore = [GlassDockM0SafetyNative]::GetForegroundWindow() -eq $originalHwnd
            $focusDetails += "; verified=$focusRestore; attempts=12"
        }
    }
    Add-M0InteractiveRestorationCheck $Session 'Foreground window restored' $focusRestore $focusDetails

    $cursorRestorePassed = $true
    $cursorDetails = 'No synthetic pointer movement was registered.'
    if ($Session.Manifest.CursorMovePending) {
        $currentCursor = [GlassDockM0SafetyPoint]::new()
        $cursorAvailable = [GlassDockM0SafetyNative]::GetCursorPos([ref]$currentCursor)
        if (-not $cursorAvailable) {
            $cursorRestorePassed = $false
            $cursorDetails = 'GetCursorPos failed while verifying the pointer restoration.'
        }
        elseif ([Math]::Abs($currentCursor.X - [int]$Session.Manifest.InjectedCursorX) -le 2 -and
            [Math]::Abs($currentCursor.Y - [int]$Session.Manifest.InjectedCursorY) -le 2) {
            $cursorRestorePassed = [GlassDockM0SafetyNative]::SetCursorPos([int]$Session.Manifest.OriginalCursorX,[int]$Session.Manifest.OriginalCursorY)
            $restoredCursor = [GlassDockM0SafetyPoint]::new()
            if ($cursorRestorePassed) { $cursorRestorePassed = [GlassDockM0SafetyNative]::GetCursorPos([ref]$restoredCursor) -and
                [Math]::Abs($restoredCursor.X - [int]$Session.Manifest.OriginalCursorX) -le 2 -and
                [Math]::Abs($restoredCursor.Y - [int]$Session.Manifest.OriginalCursorY) -le 2 }
            $cursorDetails = "Pointer was at last injected target=($($currentCursor.X),$($currentCursor.Y)); restored to snapshot=($($Session.Manifest.OriginalCursorX),$($Session.Manifest.OriginalCursorY)); verified=$cursorRestorePassed."
            $Session.Manifest.CursorMovePending = $false
            Write-M0SafetyManifest $Session
        }
        else {
            $cursorDetails = "Pointer moved after the last synthetic target; preserved current user position=($($currentCursor.X),$($currentCursor.Y))."
            $Session.Manifest.CursorMovePending = $false
            Write-M0SafetyManifest $Session
        }
    }
    Add-M0InteractiveRestorationCheck $Session 'Synthetic pointer position restored or user movement preserved' $cursorRestorePassed $cursorDetails

    $after = Get-M0InteractiveDesktopSnapshot
    $taskbarSame = $after.Taskbar.Visible -eq $Session.PreviousDesktopState.Taskbar.Visible -and
        ($after.Taskbar.Bounds | ConvertTo-Json -Compress) -ceq ($Session.PreviousDesktopState.Taskbar.Bounds | ConvertTo-Json -Compress)
    $screenSame = ($after.ScreenBounds | ConvertTo-Json -Compress) -ceq ($Session.PreviousDesktopState.ScreenBounds | ConvertTo-Json -Compress)
    Add-M0InteractiveRestorationCheck $Session 'Taskbar state unchanged' $taskbarSame (($after.Taskbar | ConvertTo-Json -Compress -Depth 5))
    Add-M0InteractiveRestorationCheck $Session 'Display bounds unchanged' $screenSame (($after.ScreenBounds | ConvertTo-Json -Compress -Depth 5))

    $existingSignal = if (Test-Path -LiteralPath $Session.StopPath) { Get-Content -LiteralPath $Session.StopPath -Raw | ConvertFrom-Json } else { $null }
    if (-not $existingSignal) {
        $reason = if ($Completed) { 'completed' } else { 'runner-failed' }
        [IO.File]::WriteAllText($Session.StopPath, (@{ RunId=$Session.RunId; Reason=$reason; TimestampUtc=[DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    }

    if ($Session.WatchdogIdentity) {
        $watchdogExited = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            $Session.WatchdogProcess.Refresh()
            if ($Session.WatchdogProcess.HasExited) { $watchdogExited = $true; break }
            Start-Sleep -Milliseconds 100
        }
        if (-not $watchdogExited) { [void](Stop-M0OwnedProcess $Session.WatchdogIdentity $Session.RepoRoot); $Session.WatchdogProcess.Refresh(); $watchdogExited = $Session.WatchdogProcess.HasExited }
        Add-M0InteractiveRestorationCheck $Session 'Safety watchdog exited' $watchdogExited "Watchdog PID=$($Session.WatchdogIdentity.ProcessId)"
    }

    $summary = [ordered]@{
        RunId = $Session.RunId
        Purpose = $Session.Purpose
        FinishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Completed = $Completed
        StopReason = if ($existingSignal) { $existingSignal.Reason } elseif ($Completed) { 'completed' } else { 'runner-failed' }
        RestorationChecks = @($Session.RestorationChecks)
        RestorePassed = @($Session.RestorationChecks | Where-Object { -not $_.Passed }).Count -eq 0
        DesktopAfter = $after
    }
    [IO.File]::WriteAllText((Join-Path $Session.EvidenceRoot 'post-state.json'), ($summary | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    Write-Output "Interactive test safety evidence: $($Session.EvidenceRoot)"
    return $summary
}
