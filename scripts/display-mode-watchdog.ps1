[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DeviceName,
    [Parameter(Mandatory)][string]$EvidenceRoot,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{32}$')][string]$RunId,
    [ValidateRange(10, 300)][int]$TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not $EvidenceRoot.StartsWith('D:\MacOS\GlassDock-Win\.local\evidence\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Display watchdog evidence must remain under the repository D: evidence root: $EvidenceRoot"
}
New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$readyPath = Join-Path $EvidenceRoot 'display-watchdog-ready.json'
$stopPath = Join-Path $EvidenceRoot 'display-watchdog-stop.json'
$resultPath = Join-Path $EvidenceRoot 'display-restore-result.json'
$logPath = Join-Path $EvidenceRoot 'display-watchdog.jsonl'

$nativeSource = @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct GlassDockM0WatchdogDevMode
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    public short SpecVersion, DriverVersion, Size, DriverExtra;
    public int Fields;
    public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
    public short Color, Duplex, YResolution, TTOption, Collate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
    public short LogPixels;
    public int BitsPerPixel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
    public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
}

[StructLayout(LayoutKind.Sequential)]
public struct GlassDockM0WatchdogMessage
{
    public IntPtr Window;
    public uint Message;
    public UIntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public int PointX;
    public int PointY;
    public uint Private;
}

public static class GlassDockM0ModeWatchdogNative
{
    [DllImport("user32.dll", EntryPoint="EnumDisplaySettingsExW", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref GlassDockM0WatchdogDevMode mode, int flags);

    [DllImport("user32.dll", EntryPoint="ChangeDisplaySettingsExW", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    public static extern int ChangeDisplaySettings(string deviceName, ref GlassDockM0WatchdogDevMode mode, IntPtr window, int flags, IntPtr parameter);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", EntryPoint="PeekMessageW", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out GlassDockM0WatchdogMessage message, IntPtr window, uint minimum, uint maximum, uint remove);
}
'@
Add-Type -TypeDefinition $nativeSource

function Write-AtomicJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Value)
    $temporary = "$Path.tmp"
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function New-CurrentMode {
    $mode = [GlassDockM0WatchdogDevMode]::new()
    $mode.DeviceName = ''
    $mode.FormName = ''
    $mode.Size = [Int16][Runtime.InteropServices.Marshal]::SizeOf([type][GlassDockM0WatchdogDevMode])
    $mode.DriverExtra = 0
    return $mode
}

function Get-CurrentMode {
    $mode = New-CurrentMode
    if (-not [GlassDockM0ModeWatchdogNative]::EnumDisplaySettings($DeviceName, -1, [ref]$mode, 0)) {
        $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "Watchdog cannot query current display mode (Win32=$code)."
    }
    return $mode
}

function Get-ModeSummary {
    param([Parameter(Mandatory)]$Mode)
    return [ordered]@{
        Width = $Mode.PelsWidth
        Height = $Mode.PelsHeight
        RefreshHz = $Mode.DisplayFrequency
        BitsPerPixel = $Mode.BitsPerPixel
        Orientation = $Mode.DisplayOrientation
        Fields = ('0x{0:X8}' -f $Mode.Fields)
        StructSize = $Mode.Size
        DeviceName = $Mode.DeviceName
    }
}

$original = Get-CurrentMode
$originalSummary = Get-ModeSummary $original
$hotKeyId = 0x4D30
$hotKeyRegistered = [GlassDockM0ModeWatchdogNative]::RegisterHotKey([IntPtr]::Zero, $hotKeyId, 0x0002 -bor 0x0004, 0x7A)
if (-not $hotKeyRegistered) {
    $code = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "Cannot register independent emergency restore hotkey Ctrl+Shift+F11 (Win32=$code). Watchdog refused to arm."
}

$ready = [ordered]@{
    RunId = $RunId
    ArmedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    DeadlineUtc = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds).ToString('O')
    DeviceName = $DeviceName
    SavedMode = $originalSummary
    ProcessId = $PID
    ExecutablePath = (Get-Process -Id $PID).Path
    TimeoutSeconds = $TimeoutSeconds
    EmergencyHotkey = 'Ctrl+Shift+F11'
    RestoreApi = 'ChangeDisplaySettingsExW with CDS_FULLSCREEN; no registry update flag'
    Ready = $true
}
Write-AtomicJson $readyPath $ready
Add-Content -LiteralPath $logPath -Value (@{ Event='Armed';AtUtc=[DateTimeOffset]::UtcNow.ToString('O');RunId=$RunId;SavedMode=$originalSummary;Hotkey=$ready.EmergencyHotkey;TimeoutSeconds=$TimeoutSeconds} | ConvertTo-Json -Compress -Depth 6) -Encoding utf8

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
$restoreReason = $null
try {
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $message = [GlassDockM0WatchdogMessage]::new()
        while ([GlassDockM0ModeWatchdogNative]::PeekMessage([ref]$message, [IntPtr]::Zero, 0x0312, 0x0312, 0x0001)) {
            if ($message.WParam.ToUInt64() -eq [uint64]$hotKeyId) {
                $restoreReason = 'EmergencyHotkey-CtrlShiftF11'
                break
            }
        }
        if ($restoreReason) { break }

        if (Test-Path -LiteralPath $stopPath) {
            try {
                $stop = Get-Content -LiteralPath $stopPath -Raw | ConvertFrom-Json
                if ($stop.RunId -eq $RunId) { $restoreReason = 'ControllerStopSignal'; break }
            }
            catch { }
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $restoreReason) { $restoreReason = 'IndependentTimeout' }

    $attempts = [System.Collections.Generic.List[object]]::new()
    $restored = $false
    $after = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $modeToRestore = $original
        $returnCode = [GlassDockM0ModeWatchdogNative]::ChangeDisplaySettings(
            $DeviceName, [ref]$modeToRestore, [IntPtr]::Zero, 0x00000004, [IntPtr]::Zero)
        $lastError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        Start-Sleep -Milliseconds 400
        $afterMode = Get-CurrentMode
        $after = Get-ModeSummary $afterMode
        $matches = $after.Width -eq $originalSummary.Width -and
            $after.Height -eq $originalSummary.Height -and
            $after.RefreshHz -eq $originalSummary.RefreshHz -and
            $after.BitsPerPixel -eq $originalSummary.BitsPerPixel
        $attempts.Add([pscustomobject]@{
            Attempt = $attempt
            ApiReturnCode = $returnCode
            ApiResult = if ($returnCode -eq 0) { 'DISP_CHANGE_SUCCESSFUL' } else { "DISP_CHANGE_$returnCode" }
            Win32LastError = $lastError
            VerifiedCurrentMode = $after
            ModeMatchesSnapshot = $matches
        })
        if ($returnCode -eq 0 -and $matches) { $restored = $true; break }
        Start-Sleep -Milliseconds 800
    }

    $result = [ordered]@{
        RunId = $RunId
        RestoredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Reason = $restoreReason
        DeviceName = $DeviceName
        SavedMode = $originalSummary
        FinalMode = $after
        RestoredAndVerified = $restored
        Attempts = @($attempts)
        PersistentRegistryUpdate = $false
    }
    Write-AtomicJson $resultPath $result
    Add-Content -LiteralPath $logPath -Value (@{Event='Restored';AtUtc=$result.RestoredAtUtc;RunId=$RunId;Reason=$restoreReason;Success=$restored;FinalMode=$after} | ConvertTo-Json -Compress -Depth 6) -Encoding utf8
    if (-not $restored) { exit 3 }
    exit 0
}
finally {
    [void][GlassDockM0ModeWatchdogNative]::UnregisterHotKey([IntPtr]::Zero, $hotKeyId)
}
