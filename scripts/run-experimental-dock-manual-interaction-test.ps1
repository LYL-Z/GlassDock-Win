[CmdletBinding()]
param(
    [switch]$ConfirmDesktopTest,
    [string]$AuthorizationPhrase,
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
$arguments = @{ Mode = 'ManualInteraction' }
if ($ConfirmDesktopTest) { $arguments.ConfirmDesktopTest = $true }
if ($PSBoundParameters.ContainsKey('AuthorizationPhrase')) { $arguments.AuthorizationPhrase = $AuthorizationPhrase }
if ($PreflightOnly) { $arguments.PreflightOnly = $true }

& (Join-Path $PSScriptRoot 'run-experimental-dock-preview-test.ps1') @arguments
