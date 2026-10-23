[CmdletBinding()]
param(
    [ValidatePattern('^\\\\\.\\DISPLAY[0-9]+$')]
    [string]$DeviceName = '\\.\DISPLAY1',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$nativeSource = @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct GlassDockNativeDevMode
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    public short SpecVersion;
    public short DriverVersion;
    public short Size;
    public short DriverExtra;
    public int Fields;
    public int PositionX;
    public int PositionY;
    public int DisplayOrientation;
    public int DisplayFixedOutput;
    public short Color;
    public short Duplex;
    public short YResolution;
    public short TTOption;
    public short Collate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
    public short LogPixels;
    public int BitsPerPixel;
    public int PelsWidth;
    public int PelsHeight;
    public int DisplayFlags;
    public int DisplayFrequency;
    public int IcmMethod;
    public int IcmIntent;
    public int MediaType;
    public int DitherType;
    public int Reserved1;
    public int Reserved2;
    public int PanningWidth;
    public int PanningHeight;
}

public static class GlassDockDisplayModeNative
{
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettingsEx(string deviceName, int modeNumber, ref GlassDockNativeDevMode mode, int flags);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    public static extern int ChangeDisplaySettingsEx(string deviceName, ref GlassDockNativeDevMode mode, IntPtr window, int flags, IntPtr parameter);
}
'@
Add-Type -TypeDefinition $nativeSource

function New-GlassDockDevMode {
    $mode = [GlassDockNativeDevMode]::new()
    $mode.DeviceName = ''
    $mode.FormName = ''
    $mode.Size = [short][Runtime.InteropServices.Marshal]::SizeOf([type][GlassDockNativeDevMode])
    $mode.DriverExtra = 0
    return $mode
}

$current = New-GlassDockDevMode
$currentOk = [GlassDockDisplayModeNative]::EnumDisplaySettingsEx($DeviceName, -1, [ref]$current, 0)
if (-not $currentOk) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "EnumDisplaySettingsEx failed for $DeviceName / ENUM_CURRENT_SETTINGS (size=$($current.Size), Win32=$errorCode)."
}

$modes = [System.Collections.Generic.List[object]]::new()
$nativeCandidateModes = @{}
for ($index = 0; $index -lt 2048; $index++) {
    $mode = New-GlassDockDevMode
    if (-not [GlassDockDisplayModeNative]::EnumDisplaySettingsEx($DeviceName, $index, [ref]$mode, 0)) { break }
    if ($mode.PelsWidth -eq $current.PelsWidth -and $mode.PelsHeight -eq $current.PelsHeight -and
        $mode.DisplayFrequency -in @(60, 120, 240)) {
        $nativeCandidateModes[[string]$mode.DisplayFrequency] = $mode
    }
    $modes.Add([pscustomobject]@{
        Index = $index
        Width = $mode.PelsWidth
        Height = $mode.PelsHeight
        RefreshHz = $mode.DisplayFrequency
        BitsPerPixel = $mode.BitsPerPixel
        Orientation = $mode.DisplayOrientation
        Fields = ('0x{0:X8}' -f $mode.Fields)
    })
}

if (-not $OutputPath) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $script:LocalRoot "evidence\m0-gap\dpi-refresh\display-modes-$stamp.json"
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not $OutputPath.StartsWith(($script:LocalRoot.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside D:\MacOS\GlassDock-Win\.local: $OutputPath"
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null

$result = [ordered]@{
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Api = 'EnumDisplaySettingsExW'
    DeviceNameArgument = $DeviceName
    ChangesDisplayMode = $false
    CdsTestInvoked = $false
    CurrentSettings = [ordered]@{
        Width = $current.PelsWidth
        Height = $current.PelsHeight
        RefreshHz = $current.DisplayFrequency
        BitsPerPixel = $current.BitsPerPixel
        Orientation = $current.DisplayOrientation
        Fields = ('0x{0:X8}' -f $current.Fields)
    }
    SupportedModeCount = $modes.Count
    Modes = @($modes)
}
$nativeResolutionModes = @($modes | Where-Object { $_.Width -eq $current.PelsWidth -and $_.Height -eq $current.PelsHeight } |
    Group-Object RefreshHz | Sort-Object { [int]$_.Name } | ForEach-Object {
        [pscustomobject]@{ Width=$current.PelsWidth; Height=$current.PelsHeight; RefreshHz=[int]$_.Name; ModeCount=$_.Count }
    })
$cdsTestResults = [System.Collections.Generic.List[object]]::new()
foreach ($refreshHz in @(60, 120, 240)) {
    if (-not $nativeCandidateModes.ContainsKey([string]$refreshHz)) {
        $cdsTestResults.Add([pscustomobject]@{
            Width = $current.PelsWidth
            Height = $current.PelsHeight
            RefreshHz = $refreshHz
            Api = 'ChangeDisplaySettingsExW(CDS_TEST)'
            ReturnCode = $null
            Supported = $false
            Result = 'No matching mode was enumerated; CDS_TEST not called.'
            LastError = $null
        })
        continue
    }

    $testMode = $nativeCandidateModes[[string]$refreshHz]
    $testResult = [GlassDockDisplayModeNative]::ChangeDisplaySettingsEx(
        $DeviceName, [ref]$testMode, [IntPtr]::Zero, 0x00000002, [IntPtr]::Zero)
    $lastError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    $cdsTestResults.Add([pscustomobject]@{
        Width = $testMode.PelsWidth
        Height = $testMode.PelsHeight
        RefreshHz = $testMode.DisplayFrequency
        Api = 'ChangeDisplaySettingsExW(CDS_TEST)'
        ReturnCode = $testResult
        Supported = ($testResult -eq 0)
        Result = if ($testResult -eq 0) { 'DISP_CHANGE_SUCCESSFUL' } else { "DISP_CHANGE_$testResult" }
        LastError = $lastError
    })
}
$result.NativeResolutionRefreshModes = $nativeResolutionModes
$result.CdsTestResults = @($cdsTestResults)
$result.CdsTestInvoked = ($cdsTestResults | Where-Object { $null -ne $_.ReturnCode }).Count -gt 0
[IO.File]::WriteAllText($OutputPath, ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$nativeResolutionModes | Format-Table -AutoSize
$cdsTestResults | Format-Table -AutoSize
Write-Output "Evidence: $OutputPath"
