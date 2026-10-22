$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$raw = Join-Path $script:LocalRoot 'evidence'
$output = Join-Path $script:RepoRoot 'docs\dock-evidence\M0'
New-Item -ItemType Directory -Force -Path $output | Out-Null

$runtime = foreach ($file in Get-ChildItem (Join-Path $raw 'measurements') -Filter '*.csv') {
    $rows = @(Import-Csv -LiteralPath $file.FullName)
    if ($rows.Count -eq 0) { continue }
    $first = $rows[0]
    # The 09:23-09:24 UTC samples were taken during the verified 120Hz mode;
    # the 09:11-09:13 and 09:41 samples were taken at 240Hz.
    $refreshHz = if ($file.Name -match '20261022T092') { 120 } else { 240 }
    [pscustomobject]@{
        Candidate = $first.Candidate
        RefreshHz = $refreshHz
        ItemCount = [int]$first.ItemCount
        IconSizeDip = [int]$first.RequestedIconSizeDip
        Samples = $rows.Count
        MeanCpuPercent = [math]::Round((($rows | Measure-Object CpuPercentOfMachine -Average).Average), 4)
        PeakCpuPercent = [math]::Round((($rows | Measure-Object CpuPercentOfMachine -Maximum).Maximum), 4)
        MeanWorkingSetMiB = [math]::Round((($rows | Measure-Object WorkingSetBytes -Average).Average) / 1MB, 2)
        PeakWorkingSetMiB = [math]::Round((($rows | Measure-Object WorkingSetBytes -Maximum).Maximum) / 1MB, 2)
        MeanPrivateMiB = [math]::Round((($rows | Measure-Object PrivateBytes -Average).Average) / 1MB, 2)
        PeakPrivateMiB = [math]::Round((($rows | Measure-Object PrivateBytes -Maximum).Maximum) / 1MB, 2)
        SourceCsv = $file.Name
    }
}
$runtime | Sort-Object RefreshHz, Candidate, ItemCount, IconSizeDip |
    Export-Csv -LiteralPath (Join-Path $output 'runtime-summary.csv') -NoTypeInformation -Encoding utf8

$windowSource = Join-Path $raw 'window-tests-20261022T093210Z.csv'
$window = Import-Csv -LiteralPath $windowSource | ForEach-Object {
    [pscustomobject]@{
        Candidate = $_.Candidate
        Edge = $_.Edge
        ObservedWindowDpi = [int]$_.Dpi
        Topmost = [bool]::Parse($_.WindowIsTopmost)
        CenterHitDock = [bool]::Parse($_.CenterHitsDock)
        TransparentCornerHitsDock = [bool]::Parse($_.TransparentCornerHitsDock)
        AlphaHoleQueryPass = [bool]::Parse($_.AlphaHolePass)
        Method = 'WindowFromPoint ownership query; not a real click'
    }
}
$window | Export-Csv -LiteralPath (Join-Path $output 'window-hit-tests.csv') -NoTypeInformation -Encoding utf8

$gpu = foreach ($file in Get-ChildItem (Join-Path $raw 'gpu') -Filter '*.csv') {
    $rows = @(Import-Csv -LiteralPath $file.FullName)
    if ($rows.Count -eq 0) { continue }
    $first = $rows[0]
    $engineRows = @($rows | Where-Object { $_.Counter -ieq 'utilization percentage' })
    $dedicatedRows = @($rows | Where-Object { $_.Counter -ieq 'dedicated usage' })
    $sharedRows = @($rows | Where-Object { $_.Counter -ieq 'shared usage' })
    [pscustomobject]@{
        Candidate = $first.Candidate
        ItemCount = [int]$first.ItemCount
        IconSizeDip = [int]$first.RequestedIconSizeDip
        EngineTimestampSamples = @($engineRows.TimestampUtc | Sort-Object -Unique).Count
        PeakEngineUtilizationPercent = if ($engineRows.Count) {
            [math]::Round((($engineRows | Measure-Object Value -Maximum).Maximum), 3)
        } else { $null }
        DedicatedMemoryTimestampSamples = @($dedicatedRows.TimestampUtc | Sort-Object -Unique).Count
        PeakDedicatedMiB = if ($dedicatedRows.Count) {
            [math]::Round((($dedicatedRows | Measure-Object Value -Maximum).Maximum) / 1MB, 2)
        } else { $null }
        SharedMemoryTimestampSamples = @($sharedRows.TimestampUtc | Sort-Object -Unique).Count
        PeakSharedMiB = if ($sharedRows.Count) {
            [math]::Round((($sharedRows | Measure-Object Value -Maximum).Maximum) / 1MB, 2)
        } else { $null }
        SourceCsv = $file.Name
    }
}
$gpu | Sort-Object Candidate, ItemCount |
    Export-Csv -LiteralPath (Join-Path $output 'gpu-summary.csv') -NoTypeInformation -Encoding utf8

Write-Output "Sanitized M0 summaries written to $output"
