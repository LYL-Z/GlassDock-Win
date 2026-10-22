param(
    [Parameter(Mandatory)]
    [ValidateSet('Wpf', 'WinUI')]
    [string]$Candidate,
    [ValidateSet(8, 24)]
    [int]$ItemCount = 8,
    [ValidateSet(44, 58, 72)]
    [int]$IconSize = 58
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$projectFolder = if ($Candidate -eq 'Wpf') {
    Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.Wpf'
}
else {
    Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.WinUI'
}
$framework = if ($Candidate -eq 'Wpf') {
    'net10.0-windows10.0.19041.0'
}
else {
    'net10.0-windows10.0.26100.0\win-x64'
}
$exeName = if ($Candidate -eq 'Wpf') { 'GlassDock.PoC.Wpf.exe' } else { 'GlassDock.PoC.WinUI.exe' }
$exePath = Join-Path $projectFolder "bin\Release\$framework\$exeName"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Build the solution first; missing PoC executable: $exePath"
}

$oldItemCount = $env:GLASSDOCK_POC_ITEM_COUNT
$oldIconSize = $env:GLASSDOCK_POC_ICON_SIZE
$oldStateRoot = $env:GLASSDOCK_POC_STATE_ROOT
$env:GLASSDOCK_POC_ITEM_COUNT = [string]$ItemCount
$env:GLASSDOCK_POC_ICON_SIZE = [string]$IconSize
$env:GLASSDOCK_POC_STATE_ROOT = Join-Path $script:LocalRoot "poc-state\$Candidate"
try {
    Start-Process -FilePath $exePath -WorkingDirectory $projectFolder | Out-Null
    Write-Output "Started $Candidate M0 PoC ($ItemCount placeholders, requested $IconSize DIP icons). Exit from the tray menu or close the window. State/logs stay under .local."
}
finally {
    if ($null -eq $oldItemCount) { Remove-Item Env:GLASSDOCK_POC_ITEM_COUNT -ErrorAction SilentlyContinue }
    else { $env:GLASSDOCK_POC_ITEM_COUNT = $oldItemCount }
    if ($null -eq $oldIconSize) { Remove-Item Env:GLASSDOCK_POC_ICON_SIZE -ErrorAction SilentlyContinue }
    else { $env:GLASSDOCK_POC_ICON_SIZE = $oldIconSize }
    if ($null -eq $oldStateRoot) { Remove-Item Env:GLASSDOCK_POC_STATE_ROOT -ErrorAction SilentlyContinue }
    else { $env:GLASSDOCK_POC_STATE_ROOT = $oldStateRoot }
}
