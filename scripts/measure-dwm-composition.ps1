[CmdletBinding()]
param(
    [ValidateRange(1, 60)]
    [int]$DurationSeconds = 8,
    [ValidateRange(50, 5000)]
    [int]$SampleIntervalMilliseconds = 250,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$nativeSource = @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct M0DwmUnsignedRatio
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct M0DwmTimingInfo
{
    public uint CbSize;
    public M0DwmUnsignedRatio RateRefresh;
    public ulong QpcRefreshPeriod;
    public M0DwmUnsignedRatio RateCompose;
    public ulong QpcVBlank;
    public ulong CRefresh;
    public uint CDXRefresh;
    public ulong QpcCompose;
    public ulong CFrame;
    public uint CDXPresent;
    public ulong CRefreshFrame;
    public ulong CFrameSubmitted;
    public uint CDXPresentSubmitted;
    public ulong CFrameConfirmed;
    public uint CDXPresentConfirmed;
    public ulong CRefreshConfirmed;
    public uint CDXRefreshConfirmed;
    public ulong CFramesLate;
    public uint CFramesOutstanding;
    public ulong CFrameDisplayed;
    public ulong QpcFrameDisplayed;
    public ulong CRefreshFrameDisplayed;
    public ulong CFrameComplete;
    public ulong QpcFrameComplete;
    public ulong CFramePending;
    public ulong QpcFramePending;
    public ulong CFramesDisplayed;
    public ulong CFramesComplete;
    public ulong CFramesPending;
    public ulong CFramesAvailable;
    public ulong CFramesDropped;
    public ulong CFramesMissed;
    public ulong CRefreshNextDisplayed;
    public ulong CRefreshNextPresented;
    public ulong CRefreshesDisplayed;
    public ulong CRefreshesPresented;
    public ulong CRefreshStarted;
    public ulong CPixelsReceived;
    public ulong CPixelsDrawn;
    public ulong CBuffersEmpty;
}

public static class M0DwmNative
{
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    public static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref M0DwmTimingInfo timingInfo);
}
'@
Add-Type -TypeDefinition $nativeSource

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $directory = Join-Path $script:LocalRoot 'evidence\m0-gap\dwm'
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $OutputPath = Join-Path $directory "dwm-composition-$runId.jsonl"
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not $OutputPath.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "DWM probe output must be on D:. Refusing: $OutputPath"
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null

$frequency = [Diagnostics.Stopwatch]::Frequency
$startQpc = [Diagnostics.Stopwatch]::GetTimestamp()
$currentPrincipal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$callerAdminRoleEnabled = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$header = [ordered]@{
    RecordType = 'RunHeader'
    StartedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    DurationSeconds = $DurationSeconds
    SampleIntervalMilliseconds = $SampleIntervalMilliseconds
    QpcFrequency = $frequency
    ProcessId = $PID
    Api = 'DwmGetCompositionTimingInfo(hwnd=NULL)'
    Scope = 'Global DWM composition timing; not Dock-process presentation or physical panel scanout.'
    RequiresVisibleWindow = $false
    ChangesDisplaySettings = $false
    InjectsInput = $false
    ElevationRequested = $false
    CallerAdministratorRoleEnabled = $callerAdminRoleEnabled
    StructSizeBytes = [Runtime.InteropServices.Marshal]::SizeOf([type][M0DwmTimingInfo])
}
Add-Content -LiteralPath $OutputPath -Value ($header | ConvertTo-Json -Compress -Depth 5) -Encoding utf8

$sampleNumber = 0
$successfulSamples = 0
$endAt = [DateTime]::UtcNow.AddSeconds($DurationSeconds)
while ([DateTime]::UtcNow -lt $endAt) {
    $sampleNumber++
    $sampleQpcBefore = [Diagnostics.Stopwatch]::GetTimestamp()
    $info = [M0DwmTimingInfo]::new()
    $info.CbSize = [uint32][Runtime.InteropServices.Marshal]::SizeOf([type][M0DwmTimingInfo])
    $hresult = [M0DwmNative]::DwmGetCompositionTimingInfo([IntPtr]::Zero, [ref]$info)
    $sampleQpcAfter = [Diagnostics.Stopwatch]::GetTimestamp()
    $succeeded = $hresult -eq 0
    if ($succeeded) { $successfulSamples++ }
    $hrUnsigned = [uint32]($hresult -band 0xffffffffL)
    $refreshHz = if ($succeeded -and $info.RateRefresh.Denominator -gt 0) {
        [Math]::Round($info.RateRefresh.Numerator / [double]$info.RateRefresh.Denominator, 6)
    } else { $null }
    $composeHz = if ($succeeded -and $info.RateCompose.Denominator -gt 0) {
        [Math]::Round($info.RateCompose.Numerator / [double]$info.RateCompose.Denominator, 6)
    } else { $null }
    $errorMessage = if ($succeeded) { $null } else {
        $exception = [Runtime.InteropServices.Marshal]::GetExceptionForHR([int]$hresult)
        if ($exception) { $exception.Message } else { 'HRESULT failure; no message supplied by Marshal.' }
    }
    $record = [ordered]@{
        RecordType = 'Sample'
        Sample = $sampleNumber
        SampledAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        QpcBefore = $sampleQpcBefore
        QpcAfter = $sampleQpcAfter
        CallDurationMicroseconds = [Math]::Round(($sampleQpcAfter - $sampleQpcBefore) * 1000000.0 / $frequency, 3)
        HResult = ('0x{0:X8}' -f $hrUnsigned)
        HResultSigned = $hresult
        Succeeded = $succeeded
        ErrorMessage = $errorMessage
        ErrorCodeDomain = 'HRESULT; GetLastError is not used for this API.'
        RefreshRateNumerator = if ($succeeded) { $info.RateRefresh.Numerator } else { $null }
        RefreshRateDenominator = if ($succeeded) { $info.RateRefresh.Denominator } else { $null }
        RefreshHz = $refreshHz
        RefreshPeriodQpc = if ($succeeded) { $info.QpcRefreshPeriod } else { $null }
        ComposeRateNumerator = if ($succeeded) { $info.RateCompose.Numerator } else { $null }
        ComposeRateDenominator = if ($succeeded) { $info.RateCompose.Denominator } else { $null }
        ComposeHz = $composeHz
        ComposeQpc = if ($succeeded) { $info.QpcCompose } else { $null }
        FrameCount = if ($succeeded) { $info.CFrame } else { $null }
        FramesDisplayed = if ($succeeded) { $info.CFramesDisplayed } else { $null }
        FramesDropped = if ($succeeded) { $info.CFramesDropped } else { $null }
        PerFrameCountersValid = $sampleNumber -ge 2
        CounterValidityNote = if ($sampleNumber -ge 2) { 'DWM docs say cFramesDisplayed/cFramesAvailable/cFramesDropped become valid after the second API call.' } else { 'First call; selected cumulative counters are not yet documented as valid.' }
        Scope = 'Global DWM. Not attributed to GlassDock, not app render/submit interval, and not physical scanout.'
    }
    Add-Content -LiteralPath $OutputPath -Value ($record | ConvertTo-Json -Compress -Depth 5) -Encoding utf8
    Start-Sleep -Milliseconds $SampleIntervalMilliseconds
}

$summary = [ordered]@{
    RecordType = 'RunSummary'
    FinishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    RequestedSamplesApprox = $sampleNumber
    SuccessfulCalls = $successfulSamples
    FailedCalls = $sampleNumber - $successfulSamples
    Outcome = if ($successfulSamples -gt 0) { 'PASS: API returned successful global DWM timing samples; this does not validate Dock presentation performance.' } else { 'BLOCKED: no successful DWM timing sample was obtained.' }
    EvidencePath = $OutputPath
    ElapsedWallMilliseconds = [Math]::Round(([Diagnostics.Stopwatch]::GetTimestamp() - $startQpc) * 1000.0 / $frequency, 2)
}
Add-Content -LiteralPath $OutputPath -Value ($summary | ConvertTo-Json -Compress -Depth 5) -Encoding utf8
[pscustomobject]$summary | Format-List
