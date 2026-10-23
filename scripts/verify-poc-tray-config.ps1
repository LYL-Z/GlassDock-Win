[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$ConfirmInteractiveTest,
    [ValidateSet('Wpf','WinUI')][string[]]$Candidates = @('Wpf','WinUI'),
    [switch]$ConfigOnly,
    [switch]$TrayOnly,
    [ValidateRange(30,1800)][int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')
Add-Type -AssemblyName Accessibility

$nativeSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)] public struct M0TrayRect { public int Left,Top,Right,Bottom; }
[StructLayout(LayoutKind.Sequential)] public struct M0TrayPoint { public int X,Y; }
[StructLayout(LayoutKind.Sequential)] public struct M0TrayIdentifier { public uint Size; public IntPtr Hwnd; public uint Id; public Guid Guid; }
[StructLayout(LayoutKind.Sequential)] public struct M0TrayMouseInput { public int Dx,Dy; public uint MouseData,Flags,Time; public UIntPtr ExtraInfo; }
[StructLayout(LayoutKind.Explicit)] public struct M0TrayInputUnion { [FieldOffset(0)] public M0TrayMouseInput Mouse; }
[StructLayout(LayoutKind.Sequential)] public struct M0TrayInput { public uint Type; public M0TrayInputUnion Data; }
public delegate bool M0TrayEnumThreadProc(IntPtr hwnd,IntPtr parameter);
public delegate bool M0TrayEnumWindowsProc(IntPtr hwnd,IntPtr parameter);

public static class M0TrayNative {
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll")] public static extern bool EnumThreadWindows(uint threadId,M0TrayEnumThreadProc callback,IntPtr parameter);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd,out M0TrayRect rect);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll")] public static extern IntPtr GetMenu(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetMenuString(IntPtr menu,uint item,System.Text.StringBuilder text,int maxCount,uint flags);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetMenuItemRect(IntPtr hwnd,IntPtr menu,uint item,out M0TrayRect rect);
 [DllImport("oleacc.dll",PreserveSig=true)] public static extern int AccessibleObjectFromWindow(IntPtr hwnd,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out object accessible);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetCursorPos(out M0TrayPoint point);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className,string windowName);
 [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,M0TrayInput[] inputs,int size);
 [DllImport("user32.dll")] public static extern bool EnumWindows(M0TrayEnumWindowsProc callback,IntPtr parameter);
 [DllImport("shell32.dll",PreserveSig=true)] public static extern int Shell_NotifyIconGetRect(ref M0TrayIdentifier identifier,out M0TrayRect rect);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool GetClassName(IntPtr hwnd,System.Text.StringBuilder name,int maxCount);

 public static List<IntPtr> GetThreadWindows(IntPtr mainHwnd,int expectedPid) {
  var result=new List<IntPtr>();
  if(mainHwnd==IntPtr.Zero)return result;
  uint pid;var threadId=GetWindowThreadProcessId(mainHwnd,out pid);
  if(pid!=(uint)expectedPid)return result;
  M0TrayEnumThreadProc callback=(hwnd,param)=>{uint owner;GetWindowThreadProcessId(hwnd,out owner);if(owner==(uint)expectedPid)result.Add(hwnd);return true;};
  EnumThreadWindows(threadId,callback,IntPtr.Zero);GC.KeepAlive(callback);return result;
 }
 public static IntPtr FindVisibleProcessWindow(int expectedPid) {
  IntPtr found=IntPtr.Zero;
  M0TrayEnumWindowsProc callback=(hwnd,param)=>{uint owner;GetWindowThreadProcessId(hwnd,out owner);if(owner==(uint)expectedPid&&IsWindowVisible(hwnd)){found=hwnd;return false;}return true;};
  EnumWindows(callback,IntPtr.Zero);GC.KeepAlive(callback);return found;
 }
 public static List<IntPtr> GetVisibleProcessWindows(int expectedPid) {
  var result=new List<IntPtr>();
  M0TrayEnumWindowsProc callback=(hwnd,param)=>{uint owner;GetWindowThreadProcessId(hwnd,out owner);if(owner==(uint)expectedPid&&IsWindowVisible(hwnd))result.Add(hwnd);return true;};
  EnumWindows(callback,IntPtr.Zero);GC.KeepAlive(callback);return result;
 }
 public static bool TryGetIconRect(IntPtr hwnd,uint id,out M0TrayRect rect,out int hresult) {
  var identifier=new M0TrayIdentifier{Size=(uint)Marshal.SizeOf<M0TrayIdentifier>(),Hwnd=hwnd,Id=id,Guid=Guid.Empty};
  hresult=Shell_NotifyIconGetRect(ref identifier,out rect);return hresult==0;
 }
 public static bool MoveTo(int x,int y) {
  int left=GetSystemMetrics(76),top=GetSystemMetrics(77),width=Math.Max(1,GetSystemMetrics(78)),height=Math.Max(1,GetSystemMetrics(79));
  int nx=(int)Math.Round(Math.Max(0,Math.Min(65535,(x-left)*65535.0/Math.Max(1,width-1))));
  int ny=(int)Math.Round(Math.Max(0,Math.Min(65535,(y-top)*65535.0/Math.Max(1,height-1))));
  return SendMouse(0x0001|0x4000|0x8000,nx,ny);
 }
 public static bool RightDown(){return SendMouse(0x0008,0,0);}
 public static bool RightUp(){return SendMouse(0x0010,0,0);}
 public static bool LeftDown(){return SendMouse(0x0002,0,0);}
 public static bool LeftUp(){return SendMouse(0x0004,0,0);}
 private static bool SendMouse(uint flags,int dx,int dy){var input=new M0TrayInput{Type=0,Data=new M0TrayInputUnion{Mouse=new M0TrayMouseInput{Dx=dx,Dy=dy,Flags=flags,ExtraInfo=new UIntPtr(0x474C415353444F43UL)}}};var sent=SendInput(1,new[]{input},Marshal.SizeOf<M0TrayInput>());if(sent!=1)throw new InvalidOperationException("SendInput failed; count="+sent+"; Win32="+Marshal.GetLastWin32Error());return true;}
}
'@
Add-Type -TypeDefinition $nativeSource
if (-not $ConfigOnly) {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
}
if ($ConfigOnly -and $TrayOnly) { throw 'Choose only one of -ConfigOnly and -TrayOnly.' }

function Get-M0PoCPath {
    param([Parameter(Mandatory)][string]$Candidate)
    $project = if ($Candidate -eq 'Wpf') { Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.Wpf' } else { Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.WinUI' }
    $framework = if ($Candidate -eq 'Wpf') { 'net10.0-windows10.0.19041.0' } else { 'net10.0-windows10.0.26100.0\win-x64' }
    $exe = if ($Candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
    $path = Join-Path $project "bin\Release\$framework\$exe"
    if (-not (Test-Path -LiteralPath $path)) { throw "Build candidate first: $path" }
    return [pscustomobject]@{ Project=$project; Path=$path }
}

function Get-M0VisibleWindow {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)
    for ($attempt=0; $attempt -lt 100; $attempt++) {
        $Process.Refresh()
        if ($Process.HasExited) { throw "PoC exited during startup; exit=$($Process.ExitCode)." }
        $hwnd=[M0TrayNative]::FindVisibleProcessWindow($Process.Id)
        if ($hwnd -ne [IntPtr]::Zero) { return $hwnd }
        Start-Sleep -Milliseconds 100
    }
    throw "Visible window for PID=$($Process.Id) was not found."
}

function Find-M0TrayIcon {
    param([Parameter(Mandatory)][IntPtr]$MainHwnd,[Parameter(Mandatory)][int]$ProcessId)
    $windows = [M0TrayNative]::GetThreadWindows($MainHwnd,$ProcessId)
    foreach ($hwnd in @($MainHwnd) + @($windows)) {
        foreach ($id in 1..8) {
            $rect=[M0TrayRect]::new();$hr=0
            if ([M0TrayNative]::TryGetIconRect($hwnd,[uint32]$id,[ref]$rect,[ref]$hr)) {
                return [pscustomobject]@{Hwnd=$hwnd;Id=$id;Rect=$rect;HResult=$hr}
            }
        }
    }
    return $null
}

function Invoke-M0TrayRightClick {
    param([Parameter(Mandatory)]$Session,[Parameter(Mandatory)]$Icon)
    $x=[int](($Icon.Rect.Left+$Icon.Rect.Right)/2);$y=[int](($Icon.Rect.Top+$Icon.Rect.Bottom)/2)
    Set-M0InteractiveCursorTarget $Session $x $y
    if (-not [M0TrayNative]::MoveTo($x,$y)) { throw "Could not move to notification icon ($x,$y)." }
    Start-Sleep -Milliseconds 150
    Set-M0InteractiveRightMouseButtonState $Session $true
    try {
        [void][M0TrayNative]::RightDown()
        Start-Sleep -Milliseconds 70
        [void][M0TrayNative]::RightUp()
        Set-M0InteractiveRightMouseButtonState $Session $false
    }
    finally {
        if ($Session.RightMouseButtonDown) {
            [void][M0TrayNative]::RightUp()
            Set-M0InteractiveRightMouseButtonState $Session $false
        }
    }
}

function Hide-M0PoCWindowThroughApp {
    param([Parameter(Mandatory)][IntPtr]$Hwnd)
    if(-not [M0TrayNative]::PostMessage($Hwnd,0x802A,[IntPtr]::Zero,[IntPtr]::Zero)){throw "Could not post M0 test-hide message; Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())."}
    for($i=0;$i -lt 30;$i++){
        Start-Sleep -Milliseconds 50
        if(-not [M0TrayNative]::IsWindowVisible($Hwnd)){return $true}
    }
    return $false
}

function Invoke-M0TrayLeftClick {
    param([Parameter(Mandatory)]$Session,[Parameter(Mandatory)][int]$X,[Parameter(Mandatory)][int]$Y)
    Assert-M0InteractiveTestCanContinue $Session
    Set-M0InteractiveCursorTarget $Session $X $Y
    if(-not [M0TrayNative]::MoveTo($X,$Y)){throw "Could not move to click point ($X,$Y)."}
    Start-Sleep -Milliseconds 100
    Set-M0InteractiveMouseButtonState $Session $true
    try {
        [void][M0TrayNative]::LeftDown()
        Start-Sleep -Milliseconds 60
        [void][M0TrayNative]::LeftUp()
        Set-M0InteractiveMouseButtonState $Session $false
    }
    finally {
        if($Session.MouseButtonDown){[void][M0TrayNative]::LeftUp();Set-M0InteractiveMouseButtonState $Session $false}
    }
    Start-Sleep -Milliseconds 250
}

function Get-M0HiddenIconsButton {
    $taskbar=[M0TrayNative]::FindWindow('Shell_TrayWnd',$null)
    if($taskbar -eq [IntPtr]::Zero){return $null}
    $root=[System.Windows.Automation.AutomationElement]::FromHandle($taskbar)
    foreach($name in @('Show hidden icons','显示隐藏的图标','显示隐藏图标')){
        $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)
        try{$button=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)}catch{$button=$null}
        if($button){$r=$button.Current.BoundingRectangle;return [pscustomobject]@{Name=$button.Current.Name;Left=[int]$r.Left;Top=[int]$r.Top;Right=[int]($r.Left+$r.Width);Bottom=[int]($r.Top+$r.Height)}}
    }
    return $null
}

function Test-M0RectOverlap {
    param([Parameter(Mandatory)]$A,[Parameter(Mandatory)]$B)
    return $A.Left -lt $B.Right -and $A.Right -gt $B.Left -and $A.Top -lt $B.Bottom -and $A.Bottom -gt $B.Top
}

function Get-M0VisibleOverflowWindows {
    $taskbar=[M0TrayNative]::FindWindow('Shell_TrayWnd',$null)
    if($taskbar -eq [IntPtr]::Zero){return @()}
    [uint32]$explorerPid=0
    [void][M0TrayNative]::GetWindowThreadProcessId($taskbar,[ref]$explorerPid)
    $found=[System.Collections.Generic.List[object]]::new()
    foreach($hwnd in [M0TrayNative]::GetVisibleProcessWindows([int]$explorerPid)){
        $class=[Text.StringBuilder]::new(256)
        [void][M0TrayNative]::GetClassName($hwnd,$class,$class.Capacity)
        if($class.ToString() -match 'Overflow'){
            $r=[M0TrayRect]::new();[void][M0TrayNative]::GetWindowRect($hwnd,[ref]$r)
            $found.Add([pscustomobject]@{Hwnd=$hwnd;Class=$class.ToString();Rect="$($r.Left),$($r.Top)-$($r.Right),$($r.Bottom)"})
        }
    }
    return @($found)
}

function Ensure-M0TrayIconVisible {
    param([Parameter(Mandatory)]$Session,[Parameter(Mandatory)][IntPtr]$MainHwnd,[Parameter(Mandatory)][int]$ProcessId,[Parameter(Mandatory)]$Icon)
    $overflowButton=Get-M0HiddenIconsButton
    if(-not $overflowButton){throw 'Could not locate the Windows notification overflow button through the taskbar UI Automation tree.'}
    $rect=$Icon.Rect
    $iconRect=[pscustomobject]@{Left=$rect.Left;Top=$rect.Top;Right=$rect.Right;Bottom=$rect.Bottom}
    $opened=$false
    if(Test-M0RectOverlap $iconRect $overflowButton){
        $x=[int](($overflowButton.Left+$overflowButton.Right)/2);$y=[int](($overflowButton.Top+$overflowButton.Bottom)/2)
        Invoke-M0TrayLeftClick -Session $Session -X $x -Y $y
        $opened=$true
        $script:overflowOpenedByTest=$true
        $refreshed=Find-M0TrayIcon -MainHwnd $MainHwnd -ProcessId $ProcessId
        if(-not $refreshed){throw 'Notification icon rectangle disappeared after opening the hidden-icons flyout.'}
        $rect=$refreshed.Rect
        $iconRect=[pscustomobject]@{Left=$rect.Left;Top=$rect.Top;Right=$rect.Right;Bottom=$rect.Bottom}
        if(Test-M0RectOverlap $iconRect $overflowButton){throw "Shell_NotifyIconGetRect still resolves to the hidden-icons button after opening the flyout; rect=$($rect.Left),$($rect.Top)-$($rect.Right),$($rect.Bottom)."}
        $Icon=$refreshed
    }
    return [pscustomobject]@{Icon=$Icon;OverflowButton=$overflowButton;OpenedByTest=$opened}
}

function Get-M0MenuPopup {
    param([Parameter(Mandatory)][int]$ProcessId,[Parameter(Mandatory)][IntPtr]$DockHwnd,[int]$TimeoutMilliseconds=1200)
    $deadline=[DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    do {
        foreach($hwnd in [M0TrayNative]::GetVisibleProcessWindows($ProcessId)) {
            if($hwnd -eq $DockHwnd){continue}
            $class=[Text.StringBuilder]::new(256)
            [void][M0TrayNative]::GetClassName($hwnd,$class,$class.Capacity)
            if($class.ToString() -eq '#32768' -or $class.ToString().StartsWith('WindowsForms10.Window',[StringComparison]::OrdinalIgnoreCase)) { return $hwnd }
        }
        Start-Sleep -Milliseconds 75
    } while([DateTime]::UtcNow -lt $deadline)
    return [IntPtr]::Zero
}

function Get-M0MenuItem {
    param([Parameter(Mandatory)][IntPtr]$PopupHwnd,[Parameter(Mandatory)][string]$Name,[int]$TimeoutMilliseconds=1500)
    $root=[System.Windows.Automation.AutomationElement]::FromHandle($PopupHwnd)
    $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Name)
    $deadline=[DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    do {
        try {
            $element=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
            if ($element -and $element.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem) { return $element }
        } catch { }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

function Get-M0MsaaMenuItem {
    param([Parameter(Mandatory)][IntPtr]$PopupHwnd,[Parameter(Mandatory)][string]$Name)
    $iid=[Accessibility.IAccessible].GUID
    [object]$accessibleObject=$null
    $hresult=[M0TrayNative]::AccessibleObjectFromWindow($PopupHwnd,[uint32]4294967292,[ref]$iid,[ref]$accessibleObject)
    if($hresult -lt 0 -or $null -eq $accessibleObject){return $null}
    try{[Accessibility.IAccessible]$accessible=$accessibleObject}catch{return $null}
    try{$childCount=$accessible.get_accChildCount()}catch{return $null}
    for($childId=1;$childId -le $childCount;$childId++){
        $child=[object]$childId
        try{$childName=[string]$accessible.get_accName($child)}catch{continue}
        if($childName.Trim() -ne $Name){continue}
        $left=0;$top=0;$width=0;$height=0
        try{$accessible.accLocation([ref]$left,[ref]$top,[ref]$width,[ref]$height,$child)}catch{return $null}
        if($width -le 0 -or $height -le 0){return $null}
        try{$role=$accessible.get_accRole($child)}catch{$role=$null}
        return [pscustomobject]@{Name=$childName;Left=$left;Top=$top;Right=($left+$width);Bottom=($top+$height);Role=$role;ChildCount=$childCount;HResult=('0x{0:X8}' -f [uint32]$hresult)}
    }
    return $null
}

function Invoke-M0MenuItem {
    param([Parameter(Mandatory)]$Session,[Parameter(Mandatory)][IntPtr]$PopupHwnd,[Parameter(Mandatory)][string]$Name)
    $item=Get-M0MenuItem -PopupHwnd $PopupHwnd -Name $Name
    $selectionMethod='UIA bounding rectangle'
    if($item){
        if (-not $item.Current.IsEnabled) { throw "Notification menu item '$Name' is disabled." }
        $r=$item.Current.BoundingRectangle
        $left=[int]$r.Left;$top=[int]$r.Top;$right=[int]($r.Left+$r.Width);$bottom=[int]($r.Top+$r.Height)
    } else {
        $accessibleItem=Get-M0MsaaMenuItem -PopupHwnd $PopupHwnd -Name $Name
        if($accessibleItem){
            $left=$accessibleItem.Left;$top=$accessibleItem.Top;$right=$accessibleItem.Right;$bottom=$accessibleItem.Bottom
            $selectionMethod='MSAA IAccessible.accLocation'
        }else{
            $menu=[M0TrayNative]::GetMenu($PopupHwnd)
            $count=if($menu -ne [IntPtr]::Zero){[M0TrayNative]::GetMenuItemCount($menu)}else{0}
            $windowRect=[M0TrayRect]::new();[void][M0TrayNative]::GetWindowRect($PopupHwnd,[ref]$windowRect)
            $class=[Text.StringBuilder]::new(256);[void][M0TrayNative]::GetClassName($PopupHwnd,$class,$class.Capacity)
            throw "Popup '$($class.ToString())' 0x$($PopupHwnd.ToInt64().ToString('X')) did not expose '$Name' through UIA or MSAA; GetMenuCount=$count; rect=$($windowRect.Left),$($windowRect.Top)-$($windowRect.Right),$($windowRect.Bottom)."
        }
    }
    if($right -le $left -or $bottom -le $top){throw "Notification menu item '$Name' has no usable bounding rectangle."}
    $x=[int](($left+$right)/2);$y=[int](($top+$bottom)/2)
    Invoke-M0TrayLeftClick -Session $Session -X $x -Y $y
    return [pscustomobject]@{Name=$Name;Rect="$left,$top,$right,$bottom";ClickPoint="$x,$y";Selection="SendInput left click at $selectionMethod"}
}

function Get-M0SettingsPath([string]$Candidate,[string]$Root) {
    $name=if($Candidate -eq 'Wpf'){'wpf-poc-settings.json'}else{'winui-poc-settings.json'}
    return Join-Path $Root $name
}

$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$runRoot=Join-Path $script:LocalRoot "evidence\m0-gap\tray-config\$stamp"
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
Confirm-M0InteractiveTest -Confirmed:$ConfirmInteractiveTest -Purpose 'show WPF/WinUI PoCs, hide and restore them through the registered tray Show action, invoke tray Exit, and/or launch with isolated missing/corrupt/illegal settings; only test-owned processes are stopped; emergency stop Ctrl+Shift+F12'
if (-not $PSCmdlet.ShouldProcess('Current desktop', 'Show Dock PoCs briefly, operate their notification icon menus, and hide/restore the test windows')) { return }

$watchdogExe=Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
if (-not (Test-Path -LiteralPath $watchdogExe)) { throw "Safety watchdog executable missing: $watchdogExe" }
$session=Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot -Purpose 'tray Show/Exit and isolated configuration recovery for WPF and WinUI 3' -WatchdogExe $watchdogExe -TimeoutSeconds $TimeoutSeconds
$results=[System.Collections.Generic.List[object]]::new()
$completed=$false
$overflowOpenedByTest=$false
$oldStateRoot=$env:GLASSDOCK_POC_STATE_ROOT
$oldCount=$env:GLASSDOCK_POC_ITEM_COUNT
$oldSize=$env:GLASSDOCK_POC_ICON_SIZE
$oldM0TestHide=$env:GLASSDOCK_M0_TEST_ALLOW_HIDE
try {
    foreach($candidate in $Candidates) {
        $candidateInfo=Get-M0PoCPath $candidate
        if (-not $TrayOnly) {
        foreach($configuration in @('Missing','CorruptJson','IllegalEdge')) {
            Assert-M0InteractiveTestCanContinue $session
            $caseRoot=Join-Path $runRoot "$candidate\config-$configuration"
            New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null
            $settingsPath=Get-M0SettingsPath $candidate $caseRoot
            if($configuration -eq 'CorruptJson') { Set-Content -LiteralPath $settingsPath -Value '{' -Encoding utf8 }
            elseif($configuration -eq 'IllegalEdge') { Set-Content -LiteralPath $settingsPath -Value '{"Edge":"NotAnEdge"}' -Encoding utf8 }

            $env:GLASSDOCK_POC_STATE_ROOT=$caseRoot;$env:GLASSDOCK_POC_ITEM_COUNT='8';$env:GLASSDOCK_POC_ICON_SIZE='58'
            try { $process=Start-M0OwnedTestProcess -Session $session -FilePath $candidateInfo.Path -WorkingDirectory $candidateInfo.Project }
            finally {
                if($null -eq $oldStateRoot){Remove-Item Env:GLASSDOCK_POC_STATE_ROOT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_STATE_ROOT=$oldStateRoot}
                if($null -eq $oldCount){Remove-Item Env:GLASSDOCK_POC_ITEM_COUNT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ITEM_COUNT=$oldCount}
                if($null -eq $oldSize){Remove-Item Env:GLASSDOCK_POC_ICON_SIZE -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ICON_SIZE=$oldSize}
            }
            try {
                $hwnd=Get-M0VisibleWindow $process
                Start-Sleep -Milliseconds 500
                $process.Refresh()
                $text=if(Test-Path -LiteralPath (Join-Path $caseRoot 'logs\dock.log')){Get-Content -LiteralPath (Join-Path $caseRoot 'logs\dock.log') -Raw}else{''}
                $config=if(Test-Path -LiteralPath $settingsPath){Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json -ErrorAction SilentlyContinue}else{$null}
                $loadError=$text -match 'Could not load .* settings; using Bottom\.'
                $fallbackPersisted=$config -and $config.Edge -eq 'Bottom'
                $alive=$null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)
                $expectedError=($configuration -eq 'CorruptJson')
                $results.Add([pscustomobject]@{
                    Candidate=$candidate;Scenario="Config-$configuration";Pid=$process.Id;WindowVisible=[M0TrayNative]::IsWindowVisible($hwnd)
                    InputFileBefore=$configuration;LoadErrorLogged=$loadError;ErrorLogExpected=$expectedError
                    DefaultBottomPersisted=[bool]$fallbackPersisted;ProcessSurvived=[bool]$alive
                    Status=if($alive -and [M0TrayNative]::IsWindowVisible($hwnd) -and $fallbackPersisted -and ($loadError -eq $expectedError)){'PASS'}else{'FAIL'}
                    SettingsPath=$settingsPath;LogPath=(Join-Path $caseRoot 'logs\dock.log')
                })
            }
            finally { [void](Stop-M0OwnedTestProcess $session $process) }
        }
        }

        if ($ConfigOnly) { continue }
        Assert-M0InteractiveTestCanContinue $session
        $trayRoot=Join-Path $runRoot "$candidate\tray"
        New-Item -ItemType Directory -Force -Path $trayRoot | Out-Null
        $env:GLASSDOCK_POC_STATE_ROOT=$trayRoot;$env:GLASSDOCK_POC_ITEM_COUNT='8';$env:GLASSDOCK_POC_ICON_SIZE='58';$env:GLASSDOCK_M0_TEST_ALLOW_HIDE='1'
        try { $process=Start-M0OwnedTestProcess -Session $session -FilePath $candidateInfo.Path -WorkingDirectory $candidateInfo.Project }
        finally {
            if($null -eq $oldStateRoot){Remove-Item Env:GLASSDOCK_POC_STATE_ROOT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_STATE_ROOT=$oldStateRoot}
            if($null -eq $oldCount){Remove-Item Env:GLASSDOCK_POC_ITEM_COUNT -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ITEM_COUNT=$oldCount}
            if($null -eq $oldSize){Remove-Item Env:GLASSDOCK_POC_ICON_SIZE -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_POC_ICON_SIZE=$oldSize}
            if($null -eq $oldM0TestHide){Remove-Item Env:GLASSDOCK_M0_TEST_ALLOW_HIDE -ErrorAction SilentlyContinue}else{$env:GLASSDOCK_M0_TEST_ALLOW_HIDE=$oldM0TestHide}
        }
        try {
            $hwnd=Get-M0VisibleWindow $process
            $icon=Find-M0TrayIcon -MainHwnd $hwnd -ProcessId $process.Id
            if(-not $icon) {
                $results.Add([pscustomobject]@{Candidate=$candidate;Scenario='TrayIconRect';Pid=$process.Id;Status='BLOCKED';Details='Shell_NotifyIconGetRect did not find this process notification icon among its UI-thread windows.'})
            }
            else {
                $rect=$icon.Rect
                $results.Add([pscustomobject]@{Candidate=$candidate;Scenario='TrayIconRegistered';Pid=$process.Id;NotifyHwnd=$icon.Hwnd.ToInt64();NotifyId=$icon.Id;IconRect="$($rect.Left),$($rect.Top)-$($rect.Right),$($rect.Bottom)";Status='PASS'})
                $hidden=Hide-M0PoCWindowThroughApp $hwnd
                try {
                    $prepared=Ensure-M0TrayIconVisible -Session $session -MainHwnd $hwnd -ProcessId $process.Id -Icon $icon
                    $overflowOpenedByTest=$overflowOpenedByTest -or $prepared.OpenedByTest
                    $icon=$prepared.Icon
                    Invoke-M0TrayRightClick -Session $session -Icon $icon
                    $popup=Get-M0MenuPopup -ProcessId $process.Id -DockHwnd $hwnd
                    if($popup -eq [IntPtr]::Zero){throw 'Right-click did not create a visible app-owned context menu; tray icon may be in the hidden overflow area.'}
                    $showSelection=Invoke-M0MenuItem -Session $session -PopupHwnd $popup -Name 'Show Dock'
                    Start-Sleep -Milliseconds 400
                    $process.Refresh()
                    $shown=$null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) -and [M0TrayNative]::IsWindowVisible($hwnd)
                    $results.Add([pscustomobject]@{Candidate=$candidate;Scenario='Tray-Show';Pid=$process.Id;InitiallyHidden=$hidden;VisibleAfterMenu=$shown;OverflowOpenedByTest=$prepared.OpenedByTest;OverflowButtonRect="$($prepared.OverflowButton.Left),$($prepared.OverflowButton.Top)-$($prepared.OverflowButton.Right),$($prepared.OverflowButton.Bottom)";Selection=$showSelection;Status=if($hidden -and $shown){'PASS'}else{'FAIL'}})

                    $hiddenForExit=Hide-M0PoCWindowThroughApp $hwnd
                    if(-not $hiddenForExit){throw 'The candidate window did not enter its hidden state before Exit testing.'}
                    $prepared=Ensure-M0TrayIconVisible -Session $session -MainHwnd $hwnd -ProcessId $process.Id -Icon $icon
                    $overflowOpenedByTest=$overflowOpenedByTest -or $prepared.OpenedByTest
                    $icon=$prepared.Icon
                    Invoke-M0TrayRightClick -Session $session -Icon $icon
                    $popup=Get-M0MenuPopup -ProcessId $process.Id -DockHwnd $hwnd
                    if($popup -eq [IntPtr]::Zero){throw 'Right-click did not create a visible app-owned context menu for Exit.'}
                    $exitSelection=Invoke-M0MenuItem -Session $session -PopupHwnd $popup -Name 'Exit GlassDock'
                    for($i=0;$i -lt 50;$i++){$process.Refresh();if($process.HasExited){break};Start-Sleep -Milliseconds 100}
                    $exited=$process.HasExited
                    $results.Add([pscustomobject]@{Candidate=$candidate;Scenario='Tray-Exit';Pid=$process.Id;ExitedFromMenu=$exited;OverflowOpenedByTest=$prepared.OpenedByTest;Selection=$exitSelection;Status=if($exited){'PASS'}else{'FAIL'}})
                }
                catch {
                    $results.Add([pscustomobject]@{Candidate=$candidate;Scenario='Tray-Show-Exit';Pid=$process.Id;Status='BLOCKED';Details=$_.Exception.Message})
                }
            }
        }
        finally { [void](Stop-M0OwnedTestProcess $session $process) }
    }

    $results|Export-Csv -LiteralPath (Join-Path $runRoot 'tray-config-results.csv') -NoTypeInformation -Encoding utf8
    $summary=[ordered]@{
        CapturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
        EvidenceRoot=$runRoot
        Display='No resolution, DPI, or refresh change.'
        Scope='Only M0 PoC windows and process-isolated state roots; configuration inputs are newly created under this evidence directory.'
        Interaction=if($ConfigOnly){'No input injection; visible test windows and isolated configuration roots only.'}else{'Actual registered notification icon right-click and SendInput menu-item selection at UIA/MSAA-derived coordinates; overflow opened only when the shell icon rectangle overlapped the Windows hidden-icons button.'}
        Tests=@($results)
        Status=if(@($results|Where-Object Status -ne 'PASS').Count -eq 0){'PASS'}elseif(@($results|Where-Object Status -eq 'FAIL').Count -gt 0){'FAIL'}else{'BLOCKED'}
        Limits='Synthetic SendInput models mouse input; it is not a physical hand-operated mouse. Configuration test files are confined to this run evidence directory. Menu windows and shell overflow access are verified independently.'
    }
    [IO.File]::WriteAllText((Join-Path $runRoot 'tray-config-results.json'),($summary|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    $results|Format-Table Candidate,Scenario,InputFileBefore,LoadErrorLogged,DefaultBottomPersisted,InitiallyHidden,VisibleAfterMenu,ExitedFromMenu,Status -AutoSize
    Write-Output "Evidence: $(Join-Path $runRoot 'tray-config-results.json')"
    $completed=$true
}
finally {
    if($overflowOpenedByTest){
        try {
            $openOverflow=Get-M0VisibleOverflowWindows
            if($openOverflow.Count -gt 0){
                $button=Get-M0HiddenIconsButton
                if($button){$x=[int](($button.Left+$button.Right)/2);$y=[int](($button.Top+$button.Bottom)/2);Invoke-M0TrayLeftClick -Session $session -X $x -Y $y}
            }
        } catch { Write-Warning "Could not verify/restore the hidden-icons flyout: $($_.Exception.Message)" }
    }
    try { [void](Stop-M0InteractiveTestSession -Session $session -Completed $completed) }
    catch { Write-Warning "Interactive safety restoration failed; inspect $($session.EvidenceRoot): $($_.Exception.Message)" }
}
