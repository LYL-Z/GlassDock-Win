$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$localRoot = Join-Path $repoRoot '.local'
. (Join-Path $PSScriptRoot 'preview-icon-asset-preflight.ps1')

function ConvertTo-TestBigEndianUInt32 {
    param([Parameter(Mandatory)][uint32]$Value)
    return ,([byte[]]@(
        [byte](($Value -shr 24) -band 255),
        [byte](($Value -shr 16) -band 255),
        [byte](($Value -shr 8) -band 255),
        [byte]($Value -band 255)
    ))
}

function Add-TestPngChunk {
    param([Parameter(Mandatory)][string]$Type, [Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Data)

    $typeBytes = [Text.Encoding]::ASCII.GetBytes($Type)
    $crcInput = [byte[]]::new($typeBytes.Length + $Data.Length)
    [Array]::Copy($typeBytes, 0, $crcInput, 0, $typeBytes.Length)
    [Array]::Copy($Data, 0, $crcInput, $typeBytes.Length, $Data.Length)
    $chunk = [IO.MemoryStream]::new()
    try {
        $lengthBytes = ConvertTo-TestBigEndianUInt32 -Value ([uint32]$Data.Length)
        $crcBytes = ConvertTo-TestBigEndianUInt32 -Value (Get-PreviewPngCrc32 -Bytes $crcInput)
        $chunk.Write($lengthBytes, 0, $lengthBytes.Length)
        $chunk.Write($typeBytes, 0, $typeBytes.Length)
        if ($Data.Length -gt 0) { $chunk.Write($Data, 0, $Data.Length) }
        $chunk.Write($crcBytes, 0, $crcBytes.Length)
        return ,([byte[]]$chunk.ToArray())
    }
    finally { $chunk.Dispose() }
}

function New-TestRgbPngBytes {
    param([Parameter(Mandatory)][byte]$Red, [Parameter(Mandatory)][byte]$Green, [Parameter(Mandatory)][byte]$Blue)

    $scanlines = [IO.MemoryStream]::new()
    $compressed = [IO.MemoryStream]::new()
    $deflater = $null
    try {
        foreach ($row in 0..1) {
            $scanlines.WriteByte(0)
            foreach ($column in 0..1) {
                $scanlines.WriteByte($Red)
                $scanlines.WriteByte($Green)
                $scanlines.WriteByte($Blue)
            }
        }
        $raw = [byte[]]$scanlines.ToArray()
        $deflater = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionLevel]::Optimal, $true)
        $deflater.Write($raw, 0, $raw.Length)
        $deflater.Dispose()
        $deflater = $null
        $deflated = [byte[]]$compressed.ToArray()

        [uint64]$adlerA = 1
        [uint64]$adlerB = 0
        foreach ($item in $raw) {
            $adlerA = ($adlerA + [uint64]$item) % 65521
            $adlerB = ($adlerB + $adlerA) % 65521
        }
        [uint32]$adler = [uint32](($adlerB -shl 16) -bor $adlerA)
        $zlib = [byte[]]::new($deflated.Length + 6)
        $zlib[0] = 120
        $zlib[1] = 156
        [Array]::Copy($deflated, 0, $zlib, 2, $deflated.Length)
        $adlerBytes = ConvertTo-TestBigEndianUInt32 -Value $adler
        [Array]::Copy($adlerBytes, 0, $zlib, $zlib.Length - 4, 4)

        $ihdr = [byte[]]::new(13)
        $widthBytes = ConvertTo-TestBigEndianUInt32 -Value 2
        $heightBytes = ConvertTo-TestBigEndianUInt32 -Value 2
        [Array]::Copy($widthBytes, 0, $ihdr, 0, 4)
        [Array]::Copy($heightBytes, 0, $ihdr, 4, 4)
        $ihdr[8] = 8
        $ihdr[9] = 2 # PNG color type 2: truecolor RGB, no alpha channel.
        $ihdr[10] = 0
        $ihdr[11] = 0
        $ihdr[12] = 0

        $png = [IO.MemoryStream]::new()
        try {
            $signature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
            $png.Write($signature, 0, $signature.Length)
            foreach ($chunk in @(
                (Add-TestPngChunk -Type 'IHDR' -Data $ihdr),
                (Add-TestPngChunk -Type 'IDAT' -Data $zlib),
                (Add-TestPngChunk -Type 'IEND' -Data ([byte[]]@()))
            )) { $png.Write($chunk, 0, $chunk.Length) }
            return ,([byte[]]$png.ToArray())
        }
        finally { $png.Dispose() }
    }
    finally {
        if ($deflater) { $deflater.Dispose() }
        $compressed.Dispose()
        $scanlines.Dispose()
    }
}

function New-ValidFixture {
    param([Parameter(Mandatory)][string]$Root)

    $assetDirectory = Join-Path $Root 'assets'
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
    $palette = @(
        [byte[]](20, 90, 170), [byte[]](80, 110, 140), [byte[]](30, 160, 210), [byte[]](170, 170, 170)
    )
    $sourceRecords = [System.Collections.Generic.List[object]]::new()
    $mappings = [System.Collections.Generic.List[object]]::new()
    $specifications = @(Get-PreviewIconAssetSpecifications)
    for ($index = 0; $index -lt $specifications.Count; $index++) {
        $specification = $specifications[$index]
        $rgb = $palette[$index]
        $pngBytes = New-TestRgbPngBytes -Red $rgb[0] -Green $rgb[1] -Blue $rgb[2]
        $assetPath = Join-Path $assetDirectory $specification.File
        [IO.File]::WriteAllBytes($assetPath, $pngBytes)
        $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $sourceRecords.Add([pscustomobject]@{
            Role = $specification.SourceRole
            File = $specification.File
            Width = 2
            Height = 2
            Bytes = $pngBytes.LongLength
            Sha256 = $hash
            Source = 'synthetic test fixture; not a product asset'
        })
        $mappings.Add([pscustomobject]@{
            selector = [pscustomobject]@{ role = $specification.SemanticRole }
            variants = @([pscustomobject]@{
                relativePath = "assets/$($specification.File)"
                theme = 'Any'
                state = $specification.VisualState
                redistributionStatus = 'LocalUseOnly'
            })
        })
    }

    [IO.File]::WriteAllText((Join-Path $Root 'manifest.json'),
        ([pscustomobject]@{ schemaVersion = 1; mappings = @($mappings) } | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $Root 'source-record.json'),
        (ConvertTo-Json -InputObject @($sourceRecords) -Depth 8),
        [Text.UTF8Encoding]::new($false))
}

function Copy-Fixture {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)
    New-Item -ItemType Directory -Path (Join-Path $Destination 'assets') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $Source 'manifest.json') -Destination $Destination
    Copy-Item -LiteralPath (Join-Path $Source 'source-record.json') -Destination $Destination
    Get-ChildItem -LiteralPath (Join-Path $Source 'assets') -File | Copy-Item -Destination (Join-Path $Destination 'assets')
}

function Read-FixtureJson {
    param([Parameter(Mandatory)][string]$Path)
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -ErrorAction Stop
}

function Write-FixtureJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Value)
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 8), [Text.UTF8Encoding]::new($false))
}

function Assert-PreflightRejected {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Name)
    try {
        [void](Invoke-PreviewIconAssetPreflight -AssetRoot $Root)
        throw "Regression failed: invalid fixture '$Name' was accepted."
    }
    catch {
        if ($_.Exception.Message -like "Regression failed:*") { throw }
        Write-Output "PASS: invalid fixture rejected ($Name): $($_.Exception.Message)"
    }
}

function Get-DesktopTestProcessSnapshot {
    param([Parameter(Mandatory)][string[]]$Executables)
    $targets = @($Executables | ForEach-Object { [IO.Path]::GetFullPath($_) })
    return @(Get-Process -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $processPath = [IO.Path]::GetFullPath($_.Path)
            if ($targets -contains $processPath) {
                [pscustomobject]@{ Id = $_.Id; Path = $processPath; StartTime = $_.StartTime.ToUniversalTime().Ticks }
            }
        }
        catch { }
    })
}

$testRoot = Join-Path $localRoot ('evidence\preview-preflight-tests\' + [Guid]::NewGuid().ToString('N'))
if (-not (Test-Path -LiteralPath $localRoot -PathType Container)) { throw "D: local data root is missing: $localRoot" }
if (Test-Path -LiteralPath $testRoot) { throw 'Refusing to reuse an existing unique preflight-test directory.' }
$passCount = 0

try {
    $validRoot = Join-Path $testRoot 'valid'
    New-ValidFixture -Root $validRoot
    $validResults = @(Invoke-PreviewIconAssetPreflight -AssetRoot $validRoot)
    if ($validResults.Count -ne 4) { throw "Expected four preflight mappings, got $($validResults.Count)." }

    $expected = @(
        'Finder+Default->Finder',
        'SystemSettings+Default->SystemSettings',
        'Downloads+Default->Downloads',
        'Trash+Empty->TrashEmpty'
    )
    $actual = @($validResults | ForEach-Object { '{0}+{1}->{2}' -f $_.SemanticRole, $_.VisualState, $_.SourceRole })
    foreach ($mapping in $expected) {
        if ($actual -cnotcontains $mapping) { throw "Expected role/state/source mapping was not returned: $mapping" }
    }
    Write-Output 'PASS: Finder, SystemSettings, Downloads, and Trash+Empty -> TrashEmpty pass the shared complete preflight.'
    $passCount++

    $noAlpha = @($validResults | Where-Object { $_.Png.ColorType -eq 2 })
    if ($noAlpha.Count -ne 4 -or @($noAlpha | Where-Object { $_.Png.HasAlphaChannel -or $_.Png.HasTransparency -or $_.Png.TransparencyDescription -ne 'OpaqueNoAlphaOrTRNS' }).Count -ne 0) {
        throw 'RGB PNG fixtures were not reported with their actual no-alpha/no-transparency properties.'
    }
    Write-Output 'PASS: RGB PNG color type 2 is reported as no alpha channel, no tRNS, and opaque.'
    $passCount++

    $wrongSourceRoot = Join-Path $testRoot 'wrong-source-role'
    Copy-Fixture -Source $validRoot -Destination $wrongSourceRoot
    $wrongSource = @(Read-FixtureJson -Path (Join-Path $wrongSourceRoot 'source-record.json'))
    $wrongTrashSource = $wrongSource | Where-Object File -eq 'trash-empty.png'
    $wrongTrashSource.Role = 'Trash'
    Write-FixtureJson -Path (Join-Path $wrongSourceRoot 'source-record.json') -Value $wrongSource
    Assert-PreflightRejected -Root $wrongSourceRoot -Name 'wrong source role for Trash Empty'
    $passCount++

    $wrongStateRoot = Join-Path $testRoot 'wrong-state'
    Copy-Fixture -Source $validRoot -Destination $wrongStateRoot
    $wrongStateManifest = Read-FixtureJson -Path (Join-Path $wrongStateRoot 'manifest.json')
    $wrongTrashMapping = $wrongStateManifest.mappings | Where-Object { $_.selector.role -eq 'Trash' }
    $wrongTrashMapping.variants[0].state = 'Full'
    Write-FixtureJson -Path (Join-Path $wrongStateRoot 'manifest.json') -Value $wrongStateManifest
    Assert-PreflightRejected -Root $wrongStateRoot -Name 'Trash Full mapped to empty Trash asset'
    $passCount++

    $missingRoot = Join-Path $testRoot 'missing-image'
    Copy-Fixture -Source $validRoot -Destination $missingRoot
    Remove-Item -LiteralPath (Join-Path $missingRoot 'assets\finder.png') -Force
    Assert-PreflightRejected -Root $missingRoot -Name 'missing image'
    $passCount++

    $badRecordRoot = Join-Path $testRoot 'source-hash-mismatch'
    Copy-Fixture -Source $validRoot -Destination $badRecordRoot
    $badHashRecords = @(Read-FixtureJson -Path (Join-Path $badRecordRoot 'source-record.json'))
    $badHashRecord = $badHashRecords | Where-Object File -eq 'settings.png'
    $badHashRecord.Sha256 = ('0' * 64)
    Write-FixtureJson -Path (Join-Path $badRecordRoot 'source-record.json') -Value $badHashRecords
    Assert-PreflightRejected -Root $badRecordRoot -Name 'manifest/source-record hash mismatch'
    $passCount++

    $tamperedRoot = Join-Path $testRoot 'tampered-file'
    Copy-Fixture -Source $validRoot -Destination $tamperedRoot
    $tamperedPath = Join-Path $tamperedRoot 'assets\downloads.png'
    $tamperedBytes = [IO.File]::ReadAllBytes($tamperedPath)
    $tamperedBytes[$tamperedBytes.Length - 1] = $tamperedBytes[$tamperedBytes.Length - 1] -bxor 1
    [IO.File]::WriteAllBytes($tamperedPath, $tamperedBytes)
    Assert-PreflightRejected -Root $tamperedRoot -Name 'asset file tampered after hash recording'
    $passCount++

    $executablePaths = @(
        (Join-Path $repoRoot 'src\GlassDock.Experimental.Wpf\bin\Release\net10.0-windows10.0.19041.0\GlassDock.Experimental.Wpf.exe'),
        (Join-Path $repoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'),
        (Join-Path $repoRoot 'spikes\GlassDock.M0.WgcProbe\bin\Release\net10.0-windows10.0.26100.0\WgcProbe.exe')
    )
    $before = @(Get-DesktopTestProcessSnapshot -Executables $executablePaths)
    $environmentNames = @(
        'TEMP', 'TMP', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'GLASSDOCK_TEST_DATA_ROOT',
        'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO',
        'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE', 'NUGET_HTTP_CACHE_PATH', 'DOTNET_ROOT', 'PATH'
    )
    $environmentBefore = @{}
    foreach ($name in $environmentNames) { $environmentBefore[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    $runnerPath = Join-Path $PSScriptRoot 'run-experimental-dock-preview-test.ps1'
    $runnerFailure = $null
    try {
        $null = & $runnerPath -PreflightOnly -AssetRoot $missingRoot 2>&1
        throw 'Regression failed: preview runner accepted the invalid fixture.'
    }
    catch {
        if ($_.Exception.Message -like 'Regression failed:*') { throw }
        $runnerFailure = $_.Exception.Message
    }
    if ([string]::IsNullOrWhiteSpace($runnerFailure)) { throw 'The actual preview runner did not fail its invalid preflight.' }
    $after = @(Get-DesktopTestProcessSnapshot -Executables $executablePaths)
    $newProcesses = @($after | Where-Object { $before.Id -notcontains $_.Id })
    if ($newProcesses.Count -ne 0) { throw 'The rejected preflight started a Dock, watchdog, or WGC test executable.' }
    foreach ($name in $environmentNames) {
        $valueAfter = [Environment]::GetEnvironmentVariable($name, 'Process')
        if (-not [string]::Equals([string]$environmentBefore[$name], [string]$valueAfter, [StringComparison]::Ordinal)) {
            throw "The PreflightOnly failure changed process environment variable $name."
        }
    }
    Write-Output 'PASS: actual preview runner rejects bad assets before Dock/watchdog/WGC process creation and preserves process environment.'
    $passCount++

    Write-Output "PASS: $passCount preview asset preflight regression groups completed."
}
finally {
    $fullRoot = [IO.Path]::GetFullPath($testRoot)
    $allowedPrefix = [IO.Path]::GetFullPath((Join-Path $localRoot 'evidence\preview-preflight-tests')) + [IO.Path]::DirectorySeparatorChar
    if ($fullRoot.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $fullRoot -PathType Container)) {
        Remove-Item -LiteralPath $fullRoot -Recurse -Force
    }
}
