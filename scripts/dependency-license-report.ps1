$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$lockFiles = Get-ChildItem -LiteralPath $script:RepoRoot -Filter 'packages.lock.json' -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(\.local|\.tools|bin|obj)\\' }
$packages = @{}

foreach ($lockFile in $lockFiles) {
    $lock = Get-Content -LiteralPath $lockFile.FullName -Raw | ConvertFrom-Json
    foreach ($target in $lock.dependencies.PSObject.Properties) {
        foreach ($packageProperty in $target.Value.PSObject.Properties) {
            $packageId = $packageProperty.Name
            $entry = $packageProperty.Value
            $version = $entry.resolved
            if (-not $version) { continue }
            $key = "$packageId|$version"
            if (-not $packages.ContainsKey($key)) {
                $packages[$key] = [pscustomobject]@{
                    Id = $packageId
                    Version = $version
                    Type = $entry.type
                }
            }
            elseif ($entry.type -eq 'Direct') {
                $packages[$key].Type = 'Direct'
            }
        }
    }
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Third-party dependency inventory')
$lines.Add('')
$lines.Add('Generated from committed `packages.lock.json` files by `scripts/dependency-license-report.ps1`. Package license metadata is taken from the restored NuGet package `.nuspec`; custom or missing metadata must be checked manually before redistribution. This inventory does not grant a project license.')
$lines.Add('')
$lines.Add('| Package | Version | Directness | License metadata | NuGet page |')
$lines.Add('|---|---:|---|---|---|')

foreach ($package in ($packages.Values | Sort-Object Id, Version)) {
    $packageFolder = Join-Path $script:NugetPackages ("$($package.Id.ToLowerInvariant())\$($package.Version.ToLowerInvariant())")
    $nuspec = Get-ChildItem -LiteralPath $packageFolder -Filter '*.nuspec' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    $license = 'MISSING: manual review required'
    if ($nuspec) {
        [xml]$manifest = Get-Content -LiteralPath $nuspec.FullName -Raw
        $metadata = $manifest.package.metadata
        if ($metadata.license) {
            $licenseType = $metadata.license.type
            if ($licenseType -eq 'expression') { $license = [string]$metadata.license.InnerText }
            elseif ($licenseType -eq 'file') { $license = "license file: $($metadata.license.InnerText)" }
            else { $license = "custom license metadata ($licenseType): $($metadata.license.InnerText)" }
        }
        elseif ($metadata.licenseUrl) {
            $license = "legacy URL: $($metadata.licenseUrl)"
        }
    }

    $escapedId = [Uri]::EscapeDataString($package.Id)
    $lines.Add("| ``$($package.Id)`` | ``$($package.Version)`` | $($package.Type) | $license | [NuGet](https://www.nuget.org/packages/$escapedId/$($package.Version)) |")
}

$outputPath = Join-Path $script:RepoRoot 'docs\third-party-licenses.md'
Set-Content -LiteralPath $outputPath -Value $lines -Encoding utf8
Write-Output "Wrote $outputPath from $($packages.Count) locked package/version pairs."
