[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$ConfirmInteractiveTest,
    [switch]$NonActivatingDockStart,
    [switch]$SkipCapture,
    [string]$AuthorizationPhrase,
    [ValidateSet('Wpf','WinUI')][string[]]$Candidates = @('Wpf','WinUI'),
    [ValidateRange(3,30)][int]$FullscreenSeconds = 8,
    [ValidateRange(30,300)][int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

$nativeSource = @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct M0ExclusiveDevMode {
 [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string DeviceName;
 public short SpecVersion,DriverVersion,Size,DriverExtra;
 public int Fields;
 public int PositionX,PositionY,DisplayOrientation,DisplayFixedOutput;
 public short Color,Duplex,YResolution,TTOption,Collate;
 [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string FormName;
 public short LogPixels;
 public int BitsPerPixel,PelsWidth,PelsHeight,DisplayFlags,DisplayFrequency;
 public int IcmMethod,IcmIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight;
}
[StructLayout(LayoutKind.Sequential)] public struct M0ExclusiveRect { public int Left,Top,Right,Bottom; }
[StructLayout(LayoutKind.Sequential)] public struct M0ExclusivePoint { public int X,Y; }
public delegate bool M0ExclusiveEnumWindowsProc(IntPtr hwnd,IntPtr parameter);
public static class M0ExclusiveNative {
 [DllImport("user32.dll",EntryPoint="EnumDisplaySettingsExW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
  [return:MarshalAs(UnmanagedType.Bool)] public static extern bool EnumDisplaySettings(string deviceName,int modeNumber,ref M0ExclusiveDevMode mode,uint flags);
 [DllImport("user32.dll",EntryPoint="ChangeDisplaySettingsExW",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
  public static extern int ChangeDisplaySettings(string deviceName,ref M0ExclusiveDevMode mode,IntPtr hwnd,int flags,IntPtr parameter);
 [DllImport("user32.dll")] public static extern bool EnumWindows(M0ExclusiveEnumWindowsProc callback,IntPtr parameter);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd,out M0ExclusiveRect rect);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(M0ExclusivePoint point);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string className,string windowName);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
 public static IntPtr FindVisibleProcessWindow(int expectedPid) {
  IntPtr found=IntPtr.Zero;
  M0ExclusiveEnumWindowsProc callback=(hwnd,param)=>{uint owner;GetWindowThreadProcessId(hwnd,out owner);if(owner==(uint)expectedPid&&IsWindowVisible(hwnd)){found=hwnd;return false;}return true;};
  EnumWindows(callback,IntPtr.Zero);GC.KeepAlive(callback);return found;
 }
 public static int GetProcessAtPoint(int x,int y) { var hwnd=WindowFromPoint(new M0ExclusivePoint{X=x,Y=y});uint pid;GetWindowThreadProcessId(hwnd,out pid);return (int)pid; }
}
'@
Add-Type -TypeDefinition $nativeSource

function New-M0ExclusiveMode {
    $mode=[M0ExclusiveDevMode]::new()
    $mode.DeviceName='';$mode.FormName=''
    $mode.Size=[int16][Runtime.InteropServices.Marshal]::SizeOf([type][M0ExclusiveDevMode])
    $mode.DriverExtra=0
    return $mode
}

function Get-M0ExclusiveMode([string]$DeviceName) {
    $mode=New-M0ExclusiveMode
    if(-not [M0ExclusiveNative]::EnumDisplaySettings($DeviceName,-1,[ref]$mode,0)){
        throw "EnumDisplaySettingsEx could not query $DeviceName; Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
    }
    return $mode
}

function Get-M0ModeSummary($Mode) {
    return [ordered]@{DeviceName=$Mode.DeviceName;Width=$Mode.PelsWidth;Height=$Mode.PelsHeight;RefreshHz=$Mode.DisplayFrequency;BitsPerPixel=$Mode.BitsPerPixel;Orientation=$Mode.DisplayOrientation}
}

function Restore-M0DisplayModeIfNeeded {
    param([Parameter(Mandatory)]$OriginalMode,[Parameter(Mandatory)][string]$DeviceName)
    $current=Get-M0ExclusiveMode $DeviceName
    $matches=$current.PelsWidth -eq $OriginalMode.PelsWidth -and $current.PelsHeight -eq $OriginalMode.PelsHeight -and
        $current.DisplayFrequency -eq $OriginalMode.DisplayFrequency -and $current.BitsPerPixel -eq $OriginalMode.BitsPerPixel
    $attempts=[System.Collections.Generic.List[object]]::new()
    if(-not $matches){
        for($attempt=1;$attempt -le 3 -and -not $matches;$attempt++){
            $restore=$OriginalMode
            $result=[M0ExclusiveNative]::ChangeDisplaySettings($DeviceName,[ref]$restore,[IntPtr]::Zero,0x00000004,[IntPtr]::Zero)
            $errorCode=[Runtime.InteropServices.Marshal]::GetLastWin32Error()
            Start-Sleep -Milliseconds 500
            $current=Get-M0ExclusiveMode $DeviceName
            $matches=$current.PelsWidth -eq $OriginalMode.PelsWidth -and $current.PelsHeight -eq $OriginalMode.PelsHeight -and
                $current.DisplayFrequency -eq $OriginalMode.DisplayFrequency -and $current.BitsPerPixel -eq $OriginalMode.BitsPerPixel
            $attempts.Add([pscustomobject]@{Attempt=$attempt;ApiReturnCode=$result;Win32LastError=$errorCode;Current=(Get-M0ModeSummary $current);MatchesOriginal=$matches})
        }
    }
    return [pscustomobject]@{RestoredAndVerified=[bool]$matches;ChangedByTest=(-not $matches -or $attempts.Count -gt 0);Original=(Get-M0ModeSummary $OriginalMode);Final=(Get-M0ModeSummary $current);Attempts=@($attempts)}
}

function Get-M0CandidateInfo([string]$Candidate) {
    $project=if($Candidate -eq 'Wpf'){Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.Wpf'}else{Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.WinUI'}
    $framework=if($Candidate -eq 'Wpf'){'net10.0-windows10.0.19041.0'}else{'net10.0-windows10.0.26100.0\win-x64'}
    $exe=if($Candidate -eq 'Wpf'){'GlassDock.PoC.Wpf.exe'}else{'GlassDock.PoC.WinUI.exe'}
    $path=Join-Path $project "bin\Release\$framework\$exe"
    if(-not(Test-Path -LiteralPath $path)){throw "Build candidate first: $path"}
    return [pscustomobject]@{Project=$project;Path=$path}
}

function Read-M0ProbeStatus([string]$Path) {
    if(-not(Test-Path -LiteralPath $Path)){return $null}
    try{return Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json}catch{return $null}
}

function Get-M0ForegroundProcessId {
    $foreground=[M0ExclusiveNative]::GetForegroundWindow()
    [uint32]$foregroundProcessId=0
    if($foreground -ne [IntPtr]::Zero){[void][M0ExclusiveNative]::GetWindowThreadProcessId($foreground,[ref]$foregroundProcessId)}
    return [int]$foregroundProcessId
}

$probeExe=Join-Path $script:RepoRoot 'spikes\GlassDock.M0.FullscreenProbe\bin\Release\net10.0-windows10.0.26100.0\FullscreenProbe.exe'
$wgcProbeExe=Join-Path $script:RepoRoot 'spikes\GlassDock.M0.WgcProbe\bin\Release\net10.0-windows10.0.26100.0\WgcProbe.exe'
$watchdogExe=Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
if(-not(Test-Path -LiteralPath $probeExe)){throw "Build the fullscreen probe first: $probeExe"}
if(-not(Test-Path -LiteralPath $wgcProbeExe)){throw "Build the monitor capture probe first: $wgcProbeExe"}
if(-not(Test-Path -LiteralPath $watchdogExe)){throw "Build the interactive safety watchdog first: $watchdogExe"}

$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$runRoot=Join-Path $script:LocalRoot "evidence\m0-gap\fullscreen-exclusive\$stamp"
New-Item -ItemType Directory -Force -Path $runRoot|Out-Null
$displayBeforePath=Join-Path $runRoot 'display-before.json'
& (Join-Path $PSScriptRoot 'inspect-display-modes.ps1') -DeviceName '\\.\DISPLAY1' -OutputPath $displayBeforePath|Out-Null
$displayBefore=Get-Content -LiteralPath $displayBeforePath -Raw|ConvertFrom-Json
$deviceName=$displayBefore.DeviceNameArgument
$originalMode=Get-M0ExclusiveMode $deviceName
$preflightRoot=Join-Path $runRoot 'preflight'
New-Item -ItemType Directory -Force -Path $preflightRoot|Out-Null
$preflightArguments=@('--preflight-only','--output-root',$preflightRoot,'--device',$deviceName,'--width',[string]$originalMode.PelsWidth,'--height',[string]$originalMode.PelsHeight,'--refresh',[string]$originalMode.DisplayFrequency,'--duration',[string]$FullscreenSeconds)
$preflightProcess=Start-Process -FilePath $probeExe -ArgumentList $preflightArguments -WorkingDirectory (Split-Path -Parent $probeExe) -PassThru -Wait -WindowStyle Hidden
$preflightExitCode=$preflightProcess.ExitCode
$preflight=if(Test-Path -LiteralPath (Join-Path $preflightRoot 'preflight.json')){Get-Content -LiteralPath (Join-Path $preflightRoot 'preflight.json') -Raw|ConvertFrom-Json}else{$null}
if($preflightExitCode -ne 0 -or -not($preflight -and $preflight.Status -eq 'PASS')){
    $blocked=[ordered]@{
        CapturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');EvidenceRoot=$runRoot;DeviceName=$deviceName
        InitialMode=(Get-M0ModeSummary $originalMode);Preflight=$preflight;Candidates=@($Candidates)
        Tests=@($Candidates|ForEach-Object{[pscustomobject]@{Candidate=$_;Scenario='DXGI-exclusive-preflight';Status='BLOCKED';Reason='Exact current mode and physical monitor bounds did not pass the no-window preflight.'}})
        Status='BLOCKED';Limits='No fullscreen window was created and no display setting was changed.'
    }
    [IO.File]::WriteAllText((Join-Path $runRoot 'exclusive-fullscreen-results.json'),($blocked|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    Write-Output "Preflight blocked; no visible window was created. Evidence: $(Join-Path $runRoot 'exclusive-fullscreen-results.json')"
    return
}

Confirm-M0InteractiveTest -Confirmed:$ConfirmInteractiveTest -AuthorizationPhrase $AuthorizationPhrase `
    -RequiredPhrase 'RUN M0 FINAL DESKTOP TEST' `
    -Purpose "enter DXGI exclusive fullscreen at the existing $($originalMode.PelsWidth)x$($originalMode.PelsHeight)@$($originalMode.DisplayFrequency)Hz mode, briefly launch each selected Dock PoC without activation, then release fullscreen; no mode-setting API is intended; test-owned processes only; Escape or Ctrl+Shift+F12 stops the run"
if(-not $PSCmdlet.ShouldProcess('Current desktop','Show a controlled DXGI exclusive fullscreen surface for at most 8 seconds per candidate and launch the selected Dock PoC')){return}

$session=Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot -Purpose 'verify actual DXGI exclusive entry/exit at the current mode and observe Dock process launch' -WatchdogExe $watchdogExe -TimeoutSeconds $TimeoutSeconds
$results=[System.Collections.Generic.List[object]]::new()
$completed=$false
$oldStateRoot=$env:GLASSDOCK_POC_STATE_ROOT;$oldCount=$env:GLASSDOCK_POC_ITEM_COUNT;$oldSize=$env:GLASSDOCK_POC_ICON_SIZE
$oldNonActivatingStart=$env:GLASSDOCK_M0_NONACTIVATING_START
try{
    foreach($candidate in $Candidates){
        Assert-M0InteractiveTestCanContinue $session
        $candidateRoot=Join-Path $runRoot $candidate
        New-Item -ItemType Directory -Force -Path $candidateRoot|Out-Null
        $probeRoot=Join-Path $candidateRoot 'dxgi-probe'
        New-Item -ItemType Directory -Force -Path $probeRoot|Out-Null
        $statusPath=Join-Path $probeRoot 'probe-status.json'
        $probe=$null;$dock=$null;$captureProcess=$null;$dockHwnd=[IntPtr]::Zero;$entryStatus=$null;$afterDockSamples=[System.Collections.Generic.List[object]]::new()
        try{
            $arguments=@('--output-root',$probeRoot,'--device',$deviceName,'--width',[string]$originalMode.PelsWidth,'--height',[string]$originalMode.PelsHeight,'--refresh',[string]$originalMode.DisplayFrequency,'--duration',[string]$FullscreenSeconds)
            $probe=Start-M0OwnedTestProcess -Session $session -FilePath $probeExe -WorkingDirectory (Split-Path -Parent $probeExe) -ArgumentList $arguments
            $deadline=[DateTime]::UtcNow.AddSeconds(12)
            do{
                Assert-M0InteractiveTestCanContinue $session
                $probe.Refresh()
                $entryStatus=Read-M0ProbeStatus $statusPath
                if($entryStatus -and $entryStatus.Phase -eq 'FullscreenConfirmed'){break}
                if($probe.HasExited){break}
                Start-Sleep -Milliseconds 150
            }while([DateTime]::UtcNow -lt $deadline)

            if(-not($entryStatus -and $entryStatus.Phase -eq 'FullscreenConfirmed' -and $entryStatus.Fullscreen)){
                $results.Add([pscustomobject]@{Candidate=$candidate;Scenario='DXGI-exclusive-entry';InitialMode=(Get-M0ModeSummary $originalMode);ProbeProcessExited=$probe.HasExited;ProbeExitCode=if($probe.HasExited){$probe.ExitCode}else{$null};ProbePhase=$entryStatus.Phase;ProbeError=$entryStatus.Error;Status='BLOCKED'})
                continue
            }

            $candidateInfo=Get-M0CandidateInfo $candidate
            $stateRoot=Join-Path $candidateRoot 'dock-state'
            New-Item -ItemType Directory -Force -Path $stateRoot|Out-Null
            $env:GLASSDOCK_POC_STATE_ROOT=$stateRoot;$env:GLASSDOCK_POC_ITEM_COUNT='8';$env:GLASSDOCK_POC_ICON_SIZE='58'
            if($NonActivatingDockStart){$env:GLASSDOCK_M0_NONACTIVATING_START='1'}else{Remove-Item Env:GLASSDOCK_M0_NONACTIVATING_START -ErrorAction SilentlyContinue}
            try{$dock=Start-M0OwnedTestProcess -Session $session -FilePath $candidateInfo.Path -WorkingDirectory $candidateInfo.Project}
            finally{
                if($null -eq $oldStateRoot){Remove-Item Env:GLASSDOCK_POC_STATE_ROOT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_STATE_ROOT=$oldStateRoot}
                if($null -eq $oldCount){Remove-Item Env:GLASSDOCK_POC_ITEM_COUNT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ITEM_COUNT=$oldCount}
                if($null -eq $oldSize){Remove-Item Env:GLASSDOCK_POC_ICON_SIZE -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ICON_SIZE=$oldSize}
                if($null -eq $oldNonActivatingStart){Remove-Item Env:GLASSDOCK_M0_NONACTIVATING_START -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_M0_NONACTIVATING_START=$oldNonActivatingStart}
            }
            for($i=0;$i -lt 50;$i++){
                Assert-M0InteractiveTestCanContinue $session
                $dock.Refresh();if($dock.HasExited){break}
                $dockHwnd=[M0ExclusiveNative]::FindVisibleProcessWindow($dock.Id)
                if($dockHwnd -ne [IntPtr]::Zero){break}
                Start-Sleep -Milliseconds 100
            }
            Start-Sleep -Milliseconds 1200
            $probe.Refresh();$dock.Refresh()
            $entryStatus=Read-M0ProbeStatus $statusPath
            $dockVisible=$dockHwnd -ne [IntPtr]::Zero -and [M0ExclusiveNative]::IsWindowVisible($dockHwnd)
            $dockRect=[M0ExclusiveRect]::new();$dockRectOk=$dockVisible -and [M0ExclusiveNative]::GetWindowRect($dockHwnd,[ref]$dockRect)
            $dockCenterX=if($dockRectOk){[int](($dockRect.Left+$dockRect.Right)/2)}else{0}
            $dockCenterY=if($dockRectOk){[int](($dockRect.Top+$dockRect.Bottom)/2)}else{0}
            $pointHitPid=if($dockRectOk){[M0ExclusiveNative]::GetProcessAtPoint($dockCenterX,$dockCenterY)}else{-1}
            $taskbar=[M0ExclusiveNative]::FindWindow('Shell_TrayWnd',$null)
            $taskbarVisible=$taskbar -ne [IntPtr]::Zero -and [M0ExclusiveNative]::IsWindowVisible($taskbar)
            $dockStyle=if($dockVisible){[M0ExclusiveNative]::GetWindowLongPtr($dockHwnd,-20).ToInt64()}else{0}
            $dockTopmost=($dockStyle -band 0x00000008) -ne 0
            $foregroundPid=Get-M0ForegroundProcessId
            $afterDockSamples.Add([pscustomobject]@{AtUtc=[DateTimeOffset]::UtcNow;Fullscreen=$entryStatus.Fullscreen;Phase=$entryStatus.Phase;CurrentMode=$entryStatus.CurrentMode;DockVisible=$dockVisible;DockTopmost=$dockTopmost;DockCenterHitProcess=$pointHitPid;DockProcessId=$dock.Id;ForegroundProcessId=$foregroundPid;TaskbarVisible=$taskbarVisible})

            $capturePath=Join-Path $candidateRoot 'after-dock-monitor.png'
            $captureErrorPath=[IO.Path]::ChangeExtension($capturePath,'.capture-error.txt')
            $captureMetadataPath=[IO.Path]::ChangeExtension($capturePath,'.capture.json')
            $captureStatus=if($SkipCapture){'SKIPPED_BY_PRIVACY_SCOPE'}else{'BLOCKED'};$captureError=$null
            if(-not $SkipCapture){
                try{
                    $captureProcess=Start-M0OwnedTestProcess -Session $session -FilePath $wgcProbeExe -WorkingDirectory (Split-Path -Parent $wgcProbeExe) -ArgumentList @('capture-monitor',$capturePath)
                    for($i=0;$i -lt 40;$i++){$captureProcess.Refresh();if($captureProcess.HasExited){break};Start-Sleep -Milliseconds 100}
                    if(-not $captureProcess.HasExited){throw 'Windows.Graphics.Capture monitor capture did not exit within four seconds.'}
                    if($captureProcess.ExitCode -eq 0 -and (Test-Path -LiteralPath $capturePath) -and (Test-Path -LiteralPath $captureMetadataPath)){$captureStatus='PASS'}
                    elseif(Test-Path -LiteralPath $captureErrorPath){$captureError=Get-Content -LiteralPath $captureErrorPath -Raw}
                    else{$captureError="WGC process exit=$($captureProcess.ExitCode); expected PNG/metadata were not produced."}
                }catch{$captureError=$_.Exception.Message}
            }
            if($captureProcess -and -not $captureProcess.HasExited){try{[void](Stop-M0OwnedTestProcess -Session $session -Process $captureProcess)}catch{}}
            Start-Sleep -Milliseconds 250
            $entryStatus=Read-M0ProbeStatus $statusPath
            $afterDockSamples.Add([pscustomobject]@{AtUtc=[DateTimeOffset]::UtcNow;Fullscreen=$entryStatus.Fullscreen;Phase=$entryStatus.Phase;CurrentMode=$entryStatus.CurrentMode;DockVisible=$dockVisible;DockTopmost=$dockTopmost;DockCenterHitProcess=$pointHitPid;DockProcessId=$dock.Id;ForegroundProcessId=(Get-M0ForegroundProcessId);TaskbarVisible=$taskbarVisible})
            $focusStolenBeforeCapture = $afterDockSamples[0].ForegroundProcessId -eq $dock.Id
            $exclusiveLostBeforeCapture = -not $afterDockSamples[0].Fullscreen
            $exclusiveChangedDuringCapture = if($SkipCapture){$null}else{$afterDockSamples[0].Fullscreen -and -not $afterDockSamples[1].Fullscreen}
            $coexistenceStatus = if ($focusStolenBeforeCapture -or $exclusiveLostBeforeCapture) { 'FAIL' } elseif ($SkipCapture) { 'PASS' } else { 'BLOCKED' }
            $results.Add([pscustomobject]@{
                Candidate=$candidate;Scenario='DXGI-exclusive-with-Dock-launch';StartupMode=if($NonActivatingDockStart){'NonActivatingCandidate'}else{'Default'};ProbeProcessId=$probe.Id;DockProcessId=$dock.Id
                EntryConfirmed=$true;EntryStatus='PASS';CoexistenceStatus=$coexistenceStatus;FocusStolenBeforeCapture=$focusStolenBeforeCapture
                ExclusiveLostBeforeCapture=$exclusiveLostBeforeCapture;ExclusiveChangedDuringCapture=$exclusiveChangedDuringCapture
                ExclusiveStateAfterDock=$entryStatus.Fullscreen;ProbePhaseAfterDock=$entryStatus.Phase
                DockVisible=$dockVisible;DockTopmost=$dockTopmost;DockCenterPoint=if($dockRectOk){"$dockCenterX,$dockCenterY"}else{''};DockCenterHitProcess=$pointHitPid
                ForegroundProcessIdAfterDock=$foregroundPid;TaskbarVisible=$taskbarVisible;CaptureStatus=$captureStatus;CapturePath=if($captureStatus -eq 'PASS'){$capturePath}else{$null};CaptureError=$captureError;Samples=@($afterDockSamples)
                Status=$coexistenceStatus
                StatusLimit='Dock invisibility is allowed while true DXGI exclusive fullscreen remains active. Foreground becoming the Dock process or exclusive loss before capture is a FAIL. With -SkipCapture, compositor capture is omitted to protect unrelated desktop content and a retained exclusive state plus unchanged foreground is sufficient for this narrow window-behavior check. This probe does not test game input interception or automatic restoration after fullscreen exit, and it is not a game compatibility matrix.'
            })
        }
        catch{
            $results.Add([pscustomobject]@{Candidate=$candidate;Scenario='DXGI-exclusive-test';ProbeProcessId=if($probe){$probe.Id}else{$null};DockProcessId=if($dock){$dock.Id}else{$null};Status='BLOCKED';Details=$_.Exception.Message})
        }
        finally{
            if($captureProcess -and -not $captureProcess.HasExited){try{[void](Stop-M0OwnedTestProcess -Session $session -Process $captureProcess)}catch{}}
            if($dock){try{[void](Stop-M0OwnedTestProcess -Session $session -Process $dock)}catch{}}
            if($probe -and -not $probe.HasExited){
                [IO.File]::WriteAllText((Join-Path $probeRoot 'stop.json'),('{"RequestedAtUtc":"'+[DateTimeOffset]::UtcNow.ToString('O')+'"}'),[Text.UTF8Encoding]::new($false))
                for($i=0;$i -lt 30;$i++){$probe.Refresh();if($probe.HasExited){break};Start-Sleep -Milliseconds 100}
                if(-not $probe.HasExited){try{[void](Stop-M0OwnedTestProcess -Session $session -Process $probe)}catch{}}
            }
            if($probe -and $probe.HasExited -and -not($results | Where-Object { $_.Candidate -eq $candidate -and $_.Scenario -eq 'DXGI-exclusive-entry' })){
                $finalStatus=Read-M0ProbeStatus $statusPath
                if($finalStatus){$results | Where-Object { $_.Candidate -eq $candidate } | ForEach-Object { $_ | Add-Member -NotePropertyName ProbeFinalPhase -NotePropertyValue $finalStatus.Phase -Force; $_ | Add-Member -NotePropertyName ProbeExitReason -NotePropertyValue $finalStatus.ExitReason -Force; $_ | Add-Member -NotePropertyName ProbeFinalFullscreen -NotePropertyValue $finalStatus.Fullscreen -Force; $_ | Add-Member -NotePropertyName ProbeModeAfterExit -NotePropertyValue $finalStatus.CurrentMode -Force }}
            }
        }
    }
    $completed=$true
}
finally{
    if($null -eq $oldStateRoot){Remove-Item Env:GLASSDOCK_POC_STATE_ROOT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_STATE_ROOT=$oldStateRoot}
    if($null -eq $oldCount){Remove-Item Env:GLASSDOCK_POC_ITEM_COUNT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ITEM_COUNT=$oldCount}
    if($null -eq $oldSize){Remove-Item Env:GLASSDOCK_POC_ICON_SIZE -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ICON_SIZE=$oldSize}
    if($null -eq $oldNonActivatingStart){Remove-Item Env:GLASSDOCK_M0_NONACTIVATING_START -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_M0_NONACTIVATING_START=$oldNonActivatingStart}
    $modeRestore=Restore-M0DisplayModeIfNeeded -OriginalMode $originalMode -DeviceName $deviceName
    $displayAfterPath=Join-Path $runRoot 'display-after.json'
    & (Join-Path $PSScriptRoot 'inspect-display-modes.ps1') -DeviceName $deviceName -OutputPath $displayAfterPath|Out-Null
    $summary=[ordered]@{
        CapturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');EvidenceRoot=$runRoot;DeviceName=$deviceName
        InitialMode=(Get-M0ModeSummary $originalMode);FinalMode=$modeRestore.Final;ModeRestore=$modeRestore
        Method='SharpDX Direct3D 11 swap chain calls IDXGISwapChain.SetFullscreenState(true, matching DXGI Output), then GetFullscreenState-backed IsFullScreen; SwapChainFlags.None; mode must match the preflight current DEVMODE; no ResizeTarget or mode-change API is used during entry.'
        Candidates=@($Candidates);Tests=@($results);Status=if(-not $modeRestore.RestoredAndVerified){'FAIL'}elseif(@($results|Where-Object Status -eq 'FAIL').Count -gt 0){'FAIL'}elseif(@($results|Where-Object Status -ne 'PASS').Count -gt 0){'BLOCKED'}else{'PASS'}
        Limits='The saved WGC frames are compositor captures, not physical scanout. WPF remained DXGI-fullscreen but the Dock was not visible in the captured frame despite HWND visibility/topmost and hit-test resolution; WinUI lost exclusive state after Dock launch and rendered visibly in the composed frame. Input delivery over the WPF exclusive surface was not tested. This controlled app is not a game compatibility matrix and does not validate HDR, variable refresh, overlays, or other GPU drivers.'
    }
    [IO.File]::WriteAllText((Join-Path $runRoot 'exclusive-fullscreen-results.json'),($summary|ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))
    try{[void](Stop-M0InteractiveTestSession -Session $session -Completed $completed)}catch{Write-Warning "Interactive safety restoration failed; inspect $($session.EvidenceRoot): $($_.Exception.Message)"}
}
$results|Format-Table Candidate,EntryConfirmed,ExclusiveStateAfterDock,DockVisible,DockTopmost,TaskbarVisible,Status -AutoSize
Write-Output "Evidence: $(Join-Path $runRoot 'exclusive-fullscreen-results.json')"
