param(
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

Push-Location $script:RepoRoot
try {
    & (Join-Path $PSScriptRoot 'test-experimental-dock-preview-safety-static.ps1')

    if (-not $NoRestore) {
        & $script:DotnetExe restore 'GlassDock-Win.sln' --configfile 'NuGet.Config' --locked-mode
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    & $script:DotnetExe test 'GlassDock-Win.sln' --configuration Release --no-restore `
        -p:ContinuousIntegrationBuild=true --logger 'console;verbosity=normal'
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
