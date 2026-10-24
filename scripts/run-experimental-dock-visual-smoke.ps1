[CmdletBinding()]
param(
    [switch]$ConfirmDesktopTest,
    [string]$AuthorizationPhrase,
    [switch]$PreflightOnly,
    [string]$AssetRoot
)

$ErrorActionPreference = 'Stop'
$arguments = @{ Mode = 'VisualSmoke' }
if ($ConfirmDesktopTest) { $arguments.ConfirmDesktopTest = $true }
if ($PSBoundParameters.ContainsKey('AuthorizationPhrase')) { $arguments.AuthorizationPhrase = $AuthorizationPhrase }
if ($PreflightOnly) { $arguments.PreflightOnly = $true }
if ($PSBoundParameters.ContainsKey('AssetRoot')) { $arguments.AssetRoot = $AssetRoot }

& (Join-Path $PSScriptRoot 'run-experimental-dock-preview-test.ps1') @arguments
