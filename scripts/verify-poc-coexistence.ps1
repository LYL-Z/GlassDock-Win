[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$ConfirmInteractiveTest,
    [ValidateSet('Wpf','WinUI')][string[]]$Candidates = @('Wpf','WinUI'),
    [ValidateSet('Maximized','Borderless')][string[]]$Scenarios = @('Maximized','Borderless'),
    [ValidateRange(30,1800)][int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

$nativeSource = @'
using System;
using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential)] public struct M0CoexRect { public int Left,Top,Right,Bottom; }
[StructLayout(LayoutKind.Sequential)] public struct M0CoexPoint { public int X,Y; }
[StructLayout(LayoutKind.Sequential)] public struct M0CoexMouseInput { public int Dx,Dy; public uint MouseData,Flags,Time; public UIntPtr ExtraInfo; }
[StructLayout(LayoutKind.Explicit)] public struct M0CoexInputUnion { [FieldOffset(0)] public M0CoexMouseInput Mouse; }
[StructLayout(LayoutKind.Sequential)] public struct M0CoexInput { public uint Type; public M0CoexInputUnion Data; }
public delegate bool M0CoexEnumWindowsCallback(IntPtr hwnd,IntPtr parameter);
public static class M0CoexNative {
 [DllImport("user32.dll")] public static extern bool EnumWindows(M0CoexEnumWindowsCallback callback,IntPtr parameter);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd,out M0CoexRect rect);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(M0CoexPoint point);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll",SetLastError=true)] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className,string windowName);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,M0CoexInput[] inputs,int size);
 public static IntPtr FindVisibleProcessWindow(int targetPid) { IntPtr found=IntPtr.Zero; EnumWindows((hwnd,param)=>{uint pid;GetWindowThreadProcessId(hwnd,out pid);if(pid==(uint)targetPid&&IsWindowVisible(hwnd)){found=hwnd;return false;}return true;},IntPtr.Zero);return found;}
 public static int GetProcessAtPoint(int x,int y) { var hwnd=WindowFromPoint(new M0CoexPoint{X=x,Y=y});uint pid;GetWindowThreadProcessId(hwnd,out pid);return (int)pid; }
 public static void MoveTo(int x,int y) { var left=GetSystemMetrics(76);var top=GetSystemMetrics(77);var width=Math.Max(1,GetSystemMetrics(78));var height=Math.Max(1,GetSystemMetrics(79));var nx=(int)Math.Round(Math.Max(0,Math.Min(65535,(x-left)*65535.0/Math.Max(1,width-1))));var ny=(int)Math.Round(Math.Max(0,Math.Min(65535,(y-top)*65535.0/Math.Max(1,height-1))));SendMouse(0x0001|0x4000|0x8000,nx,ny); }
 public static void LeftDown() { SendMouse(0x0002,0,0); }
 public static void LeftUp() { SendMouse(0x0004,0,0); }
 private static void SendMouse(uint flags,int dx,int dy) { var input=new M0CoexInput{Type=0,Data=new M0CoexInputUnion{Mouse=new M0CoexMouseInput{Dx=dx,Dy=dy,Flags=flags,ExtraInfo=new UIntPtr(0x474C415353444F43UL)}}};var sent=SendInput(1,new[]{input},Marshal.SizeOf<M0CoexInput>());if(sent!=1)throw new InvalidOperationException("SendInput failed; count="+sent+"; Win32="+Marshal.GetLastWin32Error()); }
}
'@
Add-Type -TypeDefinition $nativeSource

function Get-M0ForegroundPid {
    $hwnd = [M0CoexNative]::GetForegroundWindow()
    [uint32]$foregroundProcessId = 0
    if ($hwnd -ne [IntPtr]::Zero) { [void][M0CoexNative]::GetWindowThreadProcessId($hwnd,[ref]$foregroundProcessId) }
    return [int]$foregroundProcessId
}

function Invoke-M0ControlledClick {
    param([Parameter(Mandatory)][int]$X,[Parameter(Mandatory)][int]$Y,[Parameter(Mandatory)]$Session)
    Assert-M0InteractiveTestCanContinue $Session
    Set-M0InteractiveCursorTarget $Session $X $Y
    [M0CoexNative]::MoveTo($X,$Y)
    Start-Sleep -Milliseconds 100
    Set-M0InteractiveMouseButtonState $Session $true
    try {
        [M0CoexNative]::LeftDown()
        Start-Sleep -Milliseconds 55
    }
    finally {
        [M0CoexNative]::LeftUp()
        Set-M0InteractiveMouseButtonState $Session $false
    }
    Start-Sleep -Milliseconds 300
}

function Get-M0PoCPath {
    param([Parameter(Mandatory)][string]$Candidate)
    $project = if ($Candidate -eq 'Wpf') { Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.Wpf' } else { Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.WinUI' }
    $framework = if ($Candidate -eq 'Wpf') { 'net10.0-windows10.0.19041.0' } else { 'net10.0-windows10.0.26100.0\win-x64' }
    $exe = if ($Candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
    $path = Join-Path $project "bin\Release\$framework\$exe"
    if (-not (Test-Path -LiteralPath $path)) { throw "Build candidate first: $path" }
    return [pscustomobject]@{ Project=$project; Path=$path }
}

$probeExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
if (-not (Test-Path -LiteralPath $probeExe)) { throw "Build the input probe first: $probeExe" }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$runRoot = Join-Path $script:LocalRoot "evidence\m0-gap\coexistence\$stamp"
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

Confirm-M0InteractiveTest -Confirmed:$ConfirmInteractiveTest -Purpose 'show only project-owned maximized and borderless full-screen background windows with WPF/WinUI Dock PoCs; change focus between those windows; record taskbar visibility and window z-order; emergency stop Ctrl+Shift+F12'
if (-not $PSCmdlet.ShouldProcess('Current desktop', 'Exercise project-owned maximized/borderless background windows and visible Dock PoCs')) { return }

$safetyWatchdogExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
$session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
    -Purpose 'coexistence: controlled maximized and borderless windows, Dock z-order, taskbar, and foreground transitions' `
    -WatchdogExe $safetyWatchdogExe -TimeoutSeconds $TimeoutSeconds
$results = [System.Collections.Generic.List[object]]::new()
$completed = $false

try {
    foreach ($scenario in $Scenarios) {
        $scenarioRoot = Join-Path $runRoot $scenario
        New-Item -ItemType Directory -Force -Path $scenarioRoot | Out-Null
        $sinkRoot = Join-Path $scenarioRoot 'lower-window'
        New-Item -ItemType Directory -Force -Path $sinkRoot | Out-Null
        $sink = Start-M0OwnedTestProcess -Session $session -FilePath $probeExe -WorkingDirectory (Split-Path -Parent $probeExe) `
            -ArgumentList @('--theme','Dark','--output-root',$sinkRoot,'--window-mode',$scenario)
        try {
            $readyPath = Join-Path $sinkRoot 'input-probe-ready.json'
            for ($i=0; $i -lt 100 -and -not (Test-Path -LiteralPath $readyPath); $i++) {
                $sink.Refresh()
                if ($sink.HasExited) { throw "Controlled $scenario window exited during startup (exit=$($sink.ExitCode))." }
                Start-Sleep -Milliseconds 100
            }
            if (-not (Test-Path -LiteralPath $readyPath)) { throw "Controlled $scenario window did not become ready." }
            $sinkReady = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
            if (-not $sinkReady.ForegroundConfirmed -or -not $sinkReady.ExpectedAreaCovered) {
                throw "Controlled $scenario window did not cover its expected region or foreground state. See $readyPath"
            }
            Set-Content -LiteralPath (Join-Path $sinkRoot 'current-scenario.txt') -Value $scenario -Encoding utf8

            foreach ($candidate in $Candidates) {
                $candidateInfo = Get-M0PoCPath $candidate
                $dockState = Join-Path $scenarioRoot "state-$candidate"
                New-Item -ItemType Directory -Force -Path $dockState | Out-Null
                $oldState=$env:GLASSDOCK_POC_STATE_ROOT; $oldCount=$env:GLASSDOCK_POC_ITEM_COUNT; $oldSize=$env:GLASSDOCK_POC_ICON_SIZE; $oldTrace=$env:GLASSDOCK_M0_INPUT_TRACE
                try {
                    $env:GLASSDOCK_POC_STATE_ROOT=$dockState
                    $env:GLASSDOCK_POC_ITEM_COUNT='8'
                    $env:GLASSDOCK_POC_ICON_SIZE='58'
                    $env:GLASSDOCK_M0_INPUT_TRACE='1'
                    $dock=Start-M0OwnedTestProcess -Session $session -FilePath $candidateInfo.Path -WorkingDirectory $candidateInfo.Project
                }
                finally {
                    if($null -eq $oldState){Remove-Item Env:GLASSDOCK_POC_STATE_ROOT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_STATE_ROOT=$oldState}
                    if($null -eq $oldCount){Remove-Item Env:GLASSDOCK_POC_ITEM_COUNT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ITEM_COUNT=$oldCount}
                    if($null -eq $oldSize){Remove-Item Env:GLASSDOCK_POC_ICON_SIZE -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ICON_SIZE=$oldSize}
                    if($null -eq $oldTrace){Remove-Item Env:GLASSDOCK_M0_INPUT_TRACE -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_M0_INPUT_TRACE=$oldTrace}
                }

                try {
                    $dockHwnd=[IntPtr]::Zero
                    for($i=0;$i -lt 100;$i++){$dock.Refresh();if($dock.HasExited){throw "$candidate exited before creating a window."};$dockHwnd=[M0CoexNative]::FindVisibleProcessWindow($dock.Id);if($dockHwnd -ne [IntPtr]::Zero){break};Start-Sleep -Milliseconds 100}
                    if($dockHwnd -eq [IntPtr]::Zero){throw "$candidate visible Dock HWND was not found."}
                    Start-Sleep -Milliseconds 500
                    $dockRect=[M0CoexRect]::new();$sinkRect=[M0CoexRect]::new()
                    if(-not [M0CoexNative]::GetWindowRect($dockHwnd,[ref]$dockRect)){throw "Dock rectangle query failed for $candidate."}
                    $sinkHwnd=[M0CoexNative]::FindVisibleProcessWindow($sink.Id)
                    if($sinkHwnd -eq [IntPtr]::Zero -or -not [M0CoexNative]::GetWindowRect($sinkHwnd,[ref]$sinkRect)){throw "Background window rectangle query failed for $scenario."}
                    $taskbar=[M0CoexNative]::FindWindow('Shell_TrayWnd',$null)
                    $taskbarRect=[M0CoexRect]::new()
                    $taskbarRectOk=$taskbar -ne [IntPtr]::Zero -and [M0CoexNative]::GetWindowRect($taskbar,[ref]$taskbarRect)
                    $taskbarCenterX=if($taskbarRectOk){[int](($taskbarRect.Left+$taskbarRect.Right)/2)}else{0}
                    $taskbarCenterY=if($taskbarRectOk){[int](($taskbarRect.Top+$taskbarRect.Bottom)/2)}else{0}
                    $dockCenterX=[int](($dockRect.Left+$dockRect.Right)/2);$dockCenterY=[int](($dockRect.Top+$dockRect.Bottom)/2)
                    $sinkCenterX=[int](($sinkRect.Left+$sinkRect.Right)/2);$sinkCenterY=[int](($sinkRect.Top+$sinkRect.Bottom)/2)
                    $dockStyle=[M0CoexNative]::GetWindowLongPtr($dockHwnd,-20).ToInt64()
                    $sinkStyle=[M0CoexNative]::GetWindowLongPtr($sinkHwnd,-16).ToInt64()
                    $centerHit=[M0CoexNative]::GetProcessAtPoint($dockCenterX,$dockCenterY)
                    $taskbarHit=if($taskbarRectOk){[M0CoexNative]::GetProcessAtPoint($taskbarCenterX,$taskbarCenterY)}else{-1}
                    $sinkIsMaximized=($scenario -ne 'Maximized') -or (($sinkStyle -band 0x01000000) -ne 0)
                    $taskbarVisible=$taskbarRectOk -and [M0CoexNative]::IsWindowVisible($taskbar)
                    $dockTopmost=($dockStyle -band 0x00000008) -ne 0
                    $sinkClickX=$sinkRect.Left+[Math]::Min(160,[Math]::Max(40,[int](($sinkRect.Right-$sinkRect.Left)/5)))
                    $sinkClickY=$sinkRect.Top+[Math]::Min(150,[Math]::Max(40,[int](($sinkRect.Bottom-$sinkRect.Top)/5)))
                    $sinkClickHit=[M0CoexNative]::GetProcessAtPoint($sinkClickX,$sinkClickY)
                    if($sinkClickHit -ne $sink.Id){throw "Safety preflight refused sink click: intended PID=$($sink.Id), point resolves to PID=$sinkClickHit, point=$sinkClickX,$sinkClickY."}
                    $sinkEventLog=Join-Path $sinkRoot 'lower-window-events.jsonl'
                    $sinkEventsBefore=if(Test-Path $sinkEventLog){@(Get-Content $sinkEventLog|Where-Object{$_}).Count}else{0}
                    Invoke-M0ControlledClick $sinkClickX $sinkClickY $session
                    $focusSinkPid=Get-M0ForegroundPid
                    $sinkEventsAfter=if(Test-Path $sinkEventLog){@(Get-Content $sinkEventLog|Where-Object{$_}).Count}else{0}
                    $dockTrace=Join-Path $dockState 'm0-input-events.jsonl'
                    $dockEventsBefore=if(Test-Path $dockTrace){@(Get-Content $dockTrace|Where-Object{$_}).Count}else{0}
                    $dockClickHit=[M0CoexNative]::GetProcessAtPoint($dockCenterX,$dockCenterY)
                    if($dockClickHit -ne $dock.Id){throw "Safety preflight refused Dock click: intended PID=$($dock.Id), point resolves to PID=$dockClickHit, point=$dockCenterX,$dockCenterY."}
                    Invoke-M0ControlledClick $dockCenterX $dockCenterY $session
                    $focusDockPid=Get-M0ForegroundPid
                    $dockEventsAfter=if(Test-Path $dockTrace){@(Get-Content $dockTrace|Where-Object{$_}).Count}else{0}
                    $results.Add([pscustomobject]@{
                        Candidate=$candidate;Scenario=$scenario;SinkPid=$sink.Id;SinkMode=$sinkReady.WindowMode;SinkState=$sinkReady.WindowState
                        SinkRect="$($sinkRect.Left),$($sinkRect.Top)-$($sinkRect.Right),$($sinkRect.Bottom)";SinkDpi=$sinkReady.Dpi;SinkIsMaximized=$sinkIsMaximized
                        DockPid=$dock.Id;DockDpi=[M0CoexNative]::GetDpiForWindow($dockHwnd);DockRect="$($dockRect.Left),$($dockRect.Top)-$($dockRect.Right),$($dockRect.Bottom)"
                        DockVisible=[M0CoexNative]::IsWindowVisible($dockHwnd);DockTopmost=$dockTopmost;DockCenterHitProcess=$centerHit;DockCenterReceivedByDock=($centerHit -eq $dock.Id)
                        TaskbarVisible=$taskbarVisible;TaskbarRect=if($taskbarRectOk){"$($taskbarRect.Left),$($taskbarRect.Top)-$($taskbarRect.Right),$($taskbarRect.Bottom)"}else{''};TaskbarCenterHitProcess=$taskbarHit
                        SinkClickPoint="$sinkClickX,$sinkClickY";SinkClickResolvedPid=$sinkClickHit;SinkClickEventDelta=($sinkEventsAfter-$sinkEventsBefore)
                        ForegroundPidAfterSinkClick=$focusSinkPid;SinkReceivedForeground=($focusSinkPid -eq $sink.Id)
                        DockClickPoint="$dockCenterX,$dockCenterY";DockClickResolvedPid=$dockClickHit;DockClickEventDelta=($dockEventsAfter-$dockEventsBefore)
                        ForegroundPidAfterDockClick=$focusDockPid;DockReceivedForeground=($focusDockPid -eq $dock.Id)
                        ExclusiveFullscreenTested=$false
                        Status=if($sinkIsMaximized -and $taskbarVisible -and $dockTopmost -and [M0CoexNative]::IsWindowVisible($dockHwnd) -and $centerHit -eq $dock.Id -and $focusSinkPid -eq $sink.Id -and $focusDockPid -eq $dock.Id -and $sinkEventsAfter -gt $sinkEventsBefore -and $dockEventsAfter -gt $dockEventsBefore){'PASS'}else{'FAIL'}
                    })
                }
                finally { if($dock){[void](Stop-M0OwnedTestProcess $session $dock)} }
            }
        }
        finally { [void](Stop-M0OwnedTestProcess $session $sink) }
    }

    $results | Export-Csv -LiteralPath (Join-Path $runRoot 'coexistence-results.csv') -NoTypeInformation -Encoding utf8
    $summary=[ordered]@{
        CapturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
        Display='Current primary display only; no mode changes.'
        Scenarios='Controlled maximized application-style window and borderless full-screen window; no exclusive DXGI fullscreen.'
        Tests=@($results)
        Status=if(@($results|Where-Object Status -ne 'PASS').Count -eq 0){'PASS for recorded maximized/borderless windows, taskbar visibility, Dock center z-order, and synthetic mouse clicks received by the sink and Dock.'}else{'FAIL; see individual rows.'}
        Limits='Foreground switching was caused by synthetic mouse clicks on project-owned sink and Dock windows, not Alt+Tab. Taskbar visibility and center hit do not prove every shell surface. Exclusive fullscreen remains untested.'
    }
    [IO.File]::WriteAllText((Join-Path $runRoot 'coexistence-results.json'),($summary|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    $results|Format-Table Candidate,Scenario,SinkDpi,DockDpi,DockTopmost,DockCenterReceivedByDock,TaskbarVisible,SinkReceivedForeground,DockReceivedForeground,Status -AutoSize
    Write-Output "Evidence: $(Join-Path $runRoot 'coexistence-results.json')"
    $completed=$true
}
finally {
    try{[void](Stop-M0InteractiveTestSession -Session $session -Completed $completed)}
    catch{Write-Warning "Interactive safety restoration failed; inspect $($session.EvidenceRoot): $($_.Exception.Message)"}
}
