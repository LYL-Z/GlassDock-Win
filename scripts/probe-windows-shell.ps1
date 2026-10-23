$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not [OperatingSystem]::IsWindows()) {
    throw 'The Windows Shell probe can run only on Windows.'
}

Push-Location $script:RepoRoot
try {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $script:DotnetExe run --project 'tools/GlassDock.ShellProbe/GlassDock.ShellProbe.csproj' `
        --configuration Release --no-build
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
