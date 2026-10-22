param(
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

Push-Location $script:RepoRoot
try {
    if (-not $NoRestore) {
        & $script:DotnetExe restore 'GlassDock-Win.sln' --configfile 'NuGet.Config' --locked-mode
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    & $script:DotnetExe build 'GlassDock-Win.sln' --configuration Release --no-restore `
        -p:ContinuousIntegrationBuild=true
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
