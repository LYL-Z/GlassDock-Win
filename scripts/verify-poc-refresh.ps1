[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$ConfirmInteractiveTest,
    [string]$DeviceName = '\\.\DISPLAY1',
    [ValidateRange(30, 180)][int]$RecoveryTimeoutSeconds = 90,
    [ValidateRange(2, 15)][int]$DwmSampleSeconds = 4,
    [ValidateSet('Wpf', 'WinUI')][string[]]$Candidates = @('Wpf', 'WinUI')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

$nativeSource = @'
using System;
using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct M0RefreshDevMode { [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string DeviceName; public short SpecVersion,DriverVersion,Size,DriverExtra; public int Fields; public int PositionX,PositionY,DisplayOrientation,DisplayFixedOutput; public short Color,Duplex,YResolution,TTOption,Collate; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string FormName; public short LogPixels; public int BitsPerPixel,PelsWidth,PelsHeight,DisplayFlags,DisplayFrequency,IcmMethod,IcmIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight; }
[StructLayout(LayoutKind.Sequential)] public struct M0RefreshRect { public int Left,Top,Right,Bottom; }
public delegate bool M0RefreshEnumWindowCallback(IntPtr hwnd, IntPtr parameter);
public static class M0RefreshNative {
 [DllImport("user32.dll",EntryPoint="EnumDisplaySettingsExW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool EnumDisplaySettings(string device,int index,ref M0RefreshDevMode mode,int flags);
 [DllImport("user32.dll",EntryPoint="ChangeDisplaySettingsExW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)] public static extern int ChangeDisplaySettings(string device,ref M0RefreshDevMode mode,IntPtr hwnd,int flags,IntPtr parameter);
 [DllImport("user32.dll")] public static extern bool EnumWindows(M0RefreshEnumWindowCallback callback,IntPtr parameter);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd,out M0RefreshRect rect);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
 public static IntPtr FindWindowForProcess(int pid) { IntPtr result=IntPtr.Zero; EnumWindows((hwnd,param)=>{ uint actual; GetWindowThreadProcessId(hwnd,out actual); if(actual==(uint)pid && IsWindowVisible(hwnd)){ result=hwnd; return false; } return true; },IntPtr.Zero); return result; }
}
'@
Add-Type -TypeDefinition $nativeSource

function New-M0RefreshDevMode {
    $mode = [M0RefreshDevMode]::new()
    $mode.DeviceName = ''
    $mode.FormName = ''
    $mode.Size = [Int16][Runtime.InteropServices.Marshal]::SizeOf([type][M0RefreshDevMode])
    return $mode
}

function Get-M0CurrentMode {
    $mode = New-M0RefreshDevMode
    if (-not [M0RefreshNative]::EnumDisplaySettings($DeviceName, -1, [ref]$mode, 0)) {
        throw "EnumDisplaySettingsExW failed for $DeviceName; Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
    }
    return $mode
}

function Get-M0ModeRecord {
    param([Parameter(Mandatory)]$Mode)
    [pscustomobject]@{ Width=$Mode.PelsWidth; Height=$Mode.PelsHeight; RefreshHz=$Mode.DisplayFrequency; BitsPerPixel=$Mode.BitsPerPixel; Fields=('0x{0:X8}' -f $Mode.Fields) }
}

function Get-M0DwmSample {
    param([Parameter(Mandatory)][string]$OutputPath)
    $meter = Join-Path $PSScriptRoot 'measure-dwm-composition.ps1'
    $hostPath = (Get-Command powershell.exe -ErrorAction Stop).Source
    $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$meter,
        '-DurationSeconds',[string]$DwmSampleSeconds,'-SampleIntervalMilliseconds','100','-OutputPath',$OutputPath)
    $quoted = $arguments | ForEach-Object { '"' + ([string]$_).Replace('"','\"') + '"' }
    $meterProcess = Start-Process -FilePath $hostPath -ArgumentList ($quoted -join ' ') -PassThru -WindowStyle Hidden -WorkingDirectory $script:RepoRoot
    $meterProcess.Refresh()
    $meterStartTicks = $meterProcess.StartTime.ToUniversalTime().Ticks
    if (-not $meterProcess.WaitForExit(($DwmSampleSeconds + 15) * 1000)) {
        $stillRunning = Get-Process -Id $meterProcess.Id -ErrorAction SilentlyContinue
        if ($stillRunning -and $stillRunning.Path -and
            [IO.Path]::GetFullPath($stillRunning.Path).Equals([IO.Path]::GetFullPath($hostPath),[StringComparison]::OrdinalIgnoreCase) -and
            $stillRunning.StartTime.ToUniversalTime().Ticks -eq $meterStartTicks) {
            Stop-Process -Id $meterProcess.Id -Force -ErrorAction SilentlyContinue
        }
        throw "DWM passive meter exceeded its bounded runtime; PID=$($meterProcess.Id)."
    }
    if ($meterProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $OutputPath)) {
        throw "DWM passive meter failed with exit code $($meterProcess.ExitCode)."
    }
    $records = @(Get-Content -LiteralPath $OutputPath | ForEach-Object { $_ | ConvertFrom-Json })
    $samples = @($records | Where-Object { $_.RecordType -eq 'Sample' -and $_.Succeeded })
    $summary = $records | Where-Object { $_.RecordType -eq 'RunSummary' } | Select-Object -Last 1
    [pscustomobject]@{
        Path = $OutputPath
        SuccessfulSamples = $samples.Count
        FailedSamples = @($records | Where-Object { $_.RecordType -eq 'Sample' -and -not $_.Succeeded }).Count
        MeanRefreshHz = if ($samples.Count) { [Math]::Round((($samples.RefreshHz | Measure-Object -Average).Average),3) } else { $null }
        MeanComposeHz = if ($samples.Count) { [Math]::Round((($samples.ComposeHz | Measure-Object -Average).Average),3) } else { $null }
        Outcome = if ($summary) { $summary.Outcome } else { 'BLOCKED: no DWM summary.' }
        Scope = 'Global DWM only; not Dock-process presentation or physical panel scanout.'
    }
}

function Invoke-M0CandidateSample {
    param(
        [Parameter(Mandatory)][string]$Candidate,
        [Parameter(Mandatory)][int]$ExpectedHz,
        [Parameter(Mandatory)][string]$EvidenceDirectory,
        [Parameter(Mandatory)]$Session
    )
    $mode = Get-M0CurrentMode
    if ($mode.DisplayFrequency -ne $ExpectedHz) { throw "Expected ${ExpectedHz}Hz but current mode is $($mode.DisplayFrequency)Hz before $Candidate test." }
    $projectFolder = if ($Candidate -eq 'Wpf') { Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.Wpf' } else { Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.WinUI' }
    $framework = if ($Candidate -eq 'Wpf') { 'net10.0-windows10.0.19041.0' } else { 'net10.0-windows10.0.26100.0\win-x64' }
    $exeName = if ($Candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
    $exePath = Join-Path $projectFolder "bin\Release\$framework\$exeName"
    if (-not (Test-Path -LiteralPath $exePath)) { throw "Build candidate before refresh test: $exePath" }
    $stateRoot = Join-Path $EvidenceDirectory "state-$Candidate"
    New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
    $oldState = $env:GLASSDOCK_POC_STATE_ROOT
    $oldCount = $env:GLASSDOCK_POC_ITEM_COUNT
    $oldSize = $env:GLASSDOCK_POC_ICON_SIZE
    try {
        $env:GLASSDOCK_POC_STATE_ROOT = $stateRoot
        $env:GLASSDOCK_POC_ITEM_COUNT = '8'
        $env:GLASSDOCK_POC_ICON_SIZE = '58'
        $dock = Start-M0OwnedTestProcess -Session $Session -FilePath $exePath -WorkingDirectory $projectFolder
    }
    finally {
        if ($null -eq $oldState) { Remove-Item Env:GLASSDOCK_POC_STATE_ROOT -ErrorAction SilentlyContinue } else { $env:GLASSDOCK_POC_STATE_ROOT=$oldState }
        if ($null -eq $oldCount) { Remove-Item Env:GLASSDOCK_POC_ITEM_COUNT -ErrorAction SilentlyContinue } else { $env:GLASSDOCK_POC_ITEM_COUNT=$oldCount }
        if ($null -eq $oldSize) { Remove-Item Env:GLASSDOCK_POC_ICON_SIZE -ErrorAction SilentlyContinue } else { $env:GLASSDOCK_POC_ICON_SIZE=$oldSize }
    }

    try {
        $hwnd = [IntPtr]::Zero
        for ($attempt=0; $attempt -lt 100; $attempt++) {
            $dock.Refresh()
            if ($dock.HasExited) { throw "$Candidate exited before presenting; exit=$($dock.ExitCode)." }
            $hwnd = [M0RefreshNative]::FindWindowForProcess($dock.Id)
            if ($hwnd -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 100
        }
        if ($hwnd -eq [IntPtr]::Zero) { throw "$Candidate window was not found." }
        Start-Sleep -Milliseconds 800
        $rect = [M0RefreshRect]::new()
        if (-not [M0RefreshNative]::GetWindowRect($hwnd,[ref]$rect)) { throw "GetWindowRect failed for $Candidate." }
        $style = [M0RefreshNative]::GetWindowLongPtr($hwnd,-20).ToInt64()
        $dwmPath = Join-Path $EvidenceDirectory "dwm-$ExpectedHz-$Candidate.jsonl"
        $dwm = Get-M0DwmSample $dwmPath
        $modeAfterCapture = Get-M0CurrentMode
        $modeStillExpected = $modeAfterCapture.PelsWidth -eq $mode.PelsWidth -and
            $modeAfterCapture.PelsHeight -eq $mode.PelsHeight -and
            $modeAfterCapture.DisplayFrequency -eq $ExpectedHz
        $dwmRefreshMatches = $dwm.SuccessfulSamples -gt 0 -and $null -ne $dwm.MeanRefreshHz -and
            [Math]::Abs([double]$dwm.MeanRefreshHz - $ExpectedHz) -le 3.0
        return [pscustomobject]@{
            Candidate = $Candidate
            DisplayMode = "$($mode.PelsWidth)x$($mode.PelsHeight)@$ExpectedHz"
            DisplayWidth = $mode.PelsWidth
            DisplayHeight = $mode.PelsHeight
            DisplayRefreshHz = $ExpectedHz
            Dpi = [M0RefreshNative]::GetDpiForWindow($hwnd)
            ProcessId = $dock.Id
            WindowHandle = ('0x{0:X}' -f $hwnd.ToInt64())
            WindowRect = "$($rect.Left),$($rect.Top)-$($rect.Right),$($rect.Bottom)"
            Topmost = (($style -band 0x00000008) -ne 0)
            DwmSamples = $dwm.SuccessfulSamples
            DwmRefreshHzMean = $dwm.MeanRefreshHz
            DwmComposeHzMean = $dwm.MeanComposeHz
            ModeStillExpectedAfterCapture = $modeStillExpected
            DwmRefreshMatchesMode = $dwmRefreshMatches
            DwmScope = $dwm.Scope
            DwmEvidence = $dwm.Path
            Status = if ($dwm.SuccessfulSamples -gt 0 -and (($style -band 0x8) -ne 0) -and $modeStillExpected -and $dwmRefreshMatches) { 'PASS' } else { 'FAIL' }
        }
    }
    finally {
        if ($dock) { [void](Stop-M0OwnedTestProcess $Session $dock) }
    }
}

$recoveryExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
if (-not (Test-Path -LiteralPath $recoveryExe)) { throw "Build the interactive safety watchdog first: $recoveryExe" }
$runStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$runDirectory = Join-Path $script:LocalRoot "evidence\m0-gap\dpi-refresh\refresh-$runStamp"
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

Confirm-M0InteractiveTest -Confirmed:$ConfirmInteractiveTest -Purpose "show the approved WPF/WinUI Dock PoCs and temporarily change the internal panel from 3840x2400@240Hz to its preflighted 120Hz mode; independent hidden rollback timeout=${RecoveryTimeoutSeconds}s; emergency restore Ctrl+Shift+F11"
if (-not $PSCmdlet.ShouldProcess('Primary display and foreground window', 'Run WPF/WinUI refresh comparison at 240Hz and temporarily at 120Hz')) { return }

$session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
    -Purpose 'WPF/WinUI foreground Dock window snapshots at 240Hz baseline and temporary 120Hz; display recovery uses a separate hidden watchdog' `
    -WatchdogExe $recoveryExe -TimeoutSeconds 900
$completed = $false
$displayWatchdog = $null
$displayRunId = [Guid]::NewGuid().ToString('N')
$displayWatchdogScript = Join-Path $PSScriptRoot 'display-mode-watchdog.ps1'
$resultPath = Join-Path $runDirectory 'refresh-results.json'
$rows = [System.Collections.Generic.List[object]]::new()
$original = Get-M0CurrentMode
$originalSummary = Get-M0ModeRecord $original
$displayChange = $null

try {
    if ($original.PelsWidth -ne 3840 -or $original.PelsHeight -ne 2400 -or $original.DisplayFrequency -ne 240) {
        throw "This run plan is pinned to the observed baseline 3840x2400@240Hz; actual mode is $($original.PelsWidth)x$($original.PelsHeight)@$($original.DisplayFrequency)."
    }

    foreach ($candidate in $Candidates) {
        $rows.Add((Invoke-M0CandidateSample -Candidate $candidate -ExpectedHz 240 -EvidenceDirectory $runDirectory -Session $session))
    }

    $target = $null
    for ($index=0; $index -lt 2048; $index++) {
        $mode = New-M0RefreshDevMode
        if (-not [M0RefreshNative]::EnumDisplaySettings($DeviceName,$index,[ref]$mode,0)) { break }
        if ($mode.PelsWidth -eq $original.PelsWidth -and $mode.PelsHeight -eq $original.PelsHeight -and $mode.DisplayFrequency -eq 120) { $target=$mode; break }
    }
    if ($null -eq $target) { throw 'Native 3840x2400@120Hz mode no longer appears in EnumDisplaySettingsExW.' }
    $testTarget = $target
    $testReturn = [M0RefreshNative]::ChangeDisplaySettings($DeviceName,[ref]$testTarget,[IntPtr]::Zero,0x00000002,[IntPtr]::Zero)
    if ($testReturn -ne 0) { throw "CDS_TEST for 120Hz mode failed: DISP_CHANGE_$testReturn." }

    $watchArguments = @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$displayWatchdogScript,
        '-DeviceName',$DeviceName,'-EvidenceRoot',$runDirectory,'-RunId',$displayRunId,'-TimeoutSeconds',[string]$RecoveryTimeoutSeconds)
    $quotedWatchArguments = $watchArguments | ForEach-Object { '"' + ([string]$_).Replace('"','\"') + '"' }
    $systemPowerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
    $displayWatchdog = Start-Process -FilePath $systemPowerShell -ArgumentList ($quotedWatchArguments -join ' ') `
        -WorkingDirectory $script:RepoRoot -PassThru -WindowStyle Hidden
    $readyPath = Join-Path $runDirectory 'display-watchdog-ready.json'
    for ($attempt=0; $attempt -lt 100 -and -not (Test-Path -LiteralPath $readyPath); $attempt++) {
        $displayWatchdog.Refresh()
        if ($displayWatchdog.HasExited) { throw "Independent display watchdog exited before arming (exit=$($displayWatchdog.ExitCode))." }
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $readyPath)) { throw 'Independent display watchdog did not arm within 10 seconds.' }
    $watchReady = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
    if ($watchReady.RunId -ne $displayRunId -or -not $watchReady.Ready -or
        $watchReady.ProcessId -ne $displayWatchdog.Id -or
        $watchReady.SavedMode.Width -ne $original.PelsWidth -or
        $watchReady.SavedMode.Height -ne $original.PelsHeight -or
        $watchReady.SavedMode.RefreshHz -ne $original.DisplayFrequency) {
        throw 'Independent watchdog snapshot did not match this process and the live original display mode.'
    }

    $targetToApply = $target
    $applyReturn = [M0RefreshNative]::ChangeDisplaySettings($DeviceName,[ref]$targetToApply,[IntPtr]::Zero,0x00000004,[IntPtr]::Zero)
    $applyError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    $current = Get-M0CurrentMode
    while ($current.DisplayFrequency -ne 120 -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $current=Get-M0CurrentMode
    }
    $displayChange = [ordered]@{
        RunId = $displayRunId
        BaselineMode = $originalSummary
        TargetMode = Get-M0ModeRecord $targetToApply
        CdsTestReturnCode = $testReturn
        ApplyReturnCode = $applyReturn
        ApplyLastError = $applyError
        CurrentModeAfterApply = Get-M0ModeRecord $current
        AppliedSuccessfully = ($applyReturn -eq 0 -and $current.DisplayFrequency -eq 120)
        PersistentRegistryUpdateRequested = $false
        IndependentWatchdogPid = $displayWatchdog.Id
        IndependentWatchdogHotkey = $watchReady.EmergencyHotkey
        IndependentWatchdogDeadline = $watchReady.DeadlineUtc
    }
    [IO.File]::WriteAllText((Join-Path $runDirectory 'mode-apply.json'),($displayChange|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    if (-not $displayChange.AppliedSuccessfully) { throw "120Hz temporary mode was not confirmed; ChangeDisplaySettingsEx return=$applyReturn." }

    foreach ($candidate in $Candidates) {
        $rows.Add((Invoke-M0CandidateSample -Candidate $candidate -ExpectedHz 120 -EvidenceDirectory $runDirectory -Session $session))
    }

    $stopSignal = @{RunId=$displayRunId;Reason='refresh-test-complete';TimestampUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $runDirectory 'display-watchdog-stop.json'),$stopSignal,[Text.UTF8Encoding]::new($false))
    if (-not $displayWatchdog.WaitForExit(12000)) { throw 'Independent display watchdog did not restore within the expected bounded window.' }
    $restoreResultPath = Join-Path $runDirectory 'display-restore-result.json'
    if (-not (Test-Path $restoreResultPath)) { throw 'Independent watchdog exited without a restoration result.' }
    $restoreResult = Get-Content -LiteralPath $restoreResultPath -Raw | ConvertFrom-Json
    $restoredCurrent = Get-M0CurrentMode
    $displayChange.Restoration = $restoreResult
    $displayChange.FinalCurrentMode = Get-M0ModeRecord $restoredCurrent
    $displayChange.RestoredToBaseline = $restoreResult.RestoredAndVerified -and
        $restoredCurrent.PelsWidth -eq $original.PelsWidth -and
        $restoredCurrent.PelsHeight -eq $original.PelsHeight -and
        $restoredCurrent.DisplayFrequency -eq $original.DisplayFrequency
    [IO.File]::WriteAllText((Join-Path $runDirectory 'mode-apply.json'),($displayChange|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    if (-not $displayChange.RestoredToBaseline) { throw 'Display mode did not independently restore to the original snapshot.' }

    $rows | Export-Csv -LiteralPath (Join-Path $runDirectory 'refresh-results.csv') -NoTypeInformation -Encoding utf8
    $summary = [ordered]@{
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        DeviceName = $DeviceName
        BaselineMode = $originalSummary
        TargetMode = '3840x2400@120Hz'
        Tests = @($rows)
        DisplayModeApplied = $displayChange.AppliedSuccessfully
        IndependentRecoveryPassed = $displayChange.RestoredToBaseline
        FrameTimeClaim = 'DWM sample is global desktop composition timing; no Dock-process Present/scanout claim is made.'
        Status = if ($displayChange.AppliedSuccessfully -and $displayChange.RestoredToBaseline -and @($rows | Where-Object Status -ne 'PASS').Count -eq 0) { 'PASS for mode API/window survival only; app present timing remains BLOCKED.' } else { 'FAIL or BLOCKED; inspect per-candidate and restoration records.' }
        UserDesktopCondition = 'Refresh rate returned to the captured baseline; no resolution/DPI registry change was requested.'
    }
    [IO.File]::WriteAllText($resultPath,($summary|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    $rows | Format-Table Candidate,DisplayMode,Dpi,Topmost,DwmSamples,DwmRefreshHzMean,DwmComposeHzMean,Status -AutoSize
    Write-Output "Evidence: $resultPath"
    $completed = $true
}
finally {
    if ($displayWatchdog -and -not $displayWatchdog.HasExited) {
        $stop = @{RunId=$displayRunId;Reason='controller-finally';TimestampUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json -Compress
        [IO.File]::WriteAllText((Join-Path $runDirectory 'display-watchdog-stop.json'),$stop,[Text.UTF8Encoding]::new($false))
        [void]$displayWatchdog.WaitForExit(12000)
    }
    if ($displayChange -and $displayChange.AppliedSuccessfully -and -not $displayChange.RestoredToBaseline) {
        $fallback = New-M0RefreshDevMode
        $fallback.PelsWidth = $original.PelsWidth
        $fallback.PelsHeight = $original.PelsHeight
        $fallback.DisplayFrequency = $original.DisplayFrequency
        $fallback.BitsPerPixel = $original.BitsPerPixel
        $fallback.DisplayOrientation = $original.DisplayOrientation
        $fallback.Fields = 0x00040000 -bor 0x00080000 -bor 0x00100000 -bor 0x00400000
        $fallbackResult = [M0RefreshNative]::ChangeDisplaySettings($DeviceName,[ref]$fallback,[IntPtr]::Zero,0x00000004,[IntPtr]::Zero)
        $fallbackMode = Get-M0CurrentMode
        $fallbackSummary = Get-M0ModeRecord $fallbackMode
        [IO.File]::WriteAllText((Join-Path $runDirectory 'parent-fallback-restore.json'),(@{ReturnCode=$fallbackResult;AtUtc=[DateTimeOffset]::UtcNow.ToString('O');Mode=$fallbackSummary}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    }
    try { [void](Stop-M0InteractiveTestSession -Session $session -Completed $completed) }
    catch { Write-Warning "Interactive safety restoration failed; inspect $($session.EvidenceRoot): $($_.Exception.Message)" }
}
