$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:LocalRoot = Join-Path $script:RepoRoot '.local'
$script:TempRoot = Join-Path $script:LocalRoot 'temp'
$script:DotnetHome = Join-Path $script:LocalRoot 'dotnet-home'
$script:NugetPackages = Join-Path $script:LocalRoot 'nuget'
$script:DotnetRoot = Join-Path $script:RepoRoot '.tools\dotnet'

foreach ($directory in @($script:TempRoot, $script:DotnetHome, $script:NugetPackages)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$env:TEMP = $script:TempRoot
$env:TMP = $script:TempRoot
$env:DOTNET_CLI_HOME = $script:DotnetHome
$env:NUGET_PACKAGES = $script:NugetPackages
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $script:LocalRoot 'http-cache'
New-Item -ItemType Directory -Force -Path $env:NUGET_HTTP_CACHE_PATH | Out-Null

$localDotnet = Join-Path $script:DotnetRoot 'dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $script:DotnetExe = $localDotnet
    $env:DOTNET_ROOT = $script:DotnetRoot
    $env:PATH = "$script:DotnetRoot;$env:PATH"
}
else {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction Stop
    $script:DotnetExe = $dotnetCommand.Source
}
