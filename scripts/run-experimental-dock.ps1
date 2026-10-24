param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

Push-Location $script:RepoRoot
try {
    & $script:DotnetExe run --project 'src/GlassDock.Experimental.Wpf/GlassDock.Experimental.Wpf.csproj' `
        --configuration $Configuration --no-restore
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
