function Read-PreviewPngUInt32BigEndian {
    param([Parameter(Mandatory)][byte[]]$Bytes, [Parameter(Mandatory)][int]$Offset)

    return ([int64]$Bytes[$Offset] * 16777216) +
        ([int64]$Bytes[$Offset + 1] * 65536) +
        ([int64]$Bytes[$Offset + 2] * 256) +
        [int64]$Bytes[$Offset + 3]
}

function Get-PreviewPngCrc32 {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    if (-not $script:PreviewPngCrcTable) {
        $script:PreviewPngCrcTable = [uint32[]]::new(256)
        for ($index = 0; $index -lt 256; $index++) {
            [uint64]$value = $index
            for ($bit = 0; $bit -lt 8; $bit++) {
                if (($value -band 1) -ne 0) { $value = ($value -shr 1) -bxor [uint64]3988292384 }
                else { $value = $value -shr 1 }
            }
            $script:PreviewPngCrcTable[$index] = [uint32]($value -band [uint64]4294967295)
        }
    }

    [uint64]$crc = 4294967295
    foreach ($value in $Bytes) {
        $tableIndex = [int](($crc -bxor [uint64]$value) -band 255)
        $crc = [uint64]$script:PreviewPngCrcTable[$tableIndex] -bxor ($crc -shr 8)
    }
    return [uint32](($crc -bxor [uint64]4294967295) -band [uint64]4294967295)
}

function Read-PreviewPngMetadata {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.Length -le 0 -or $item.Length -gt 16MB) {
        throw "PNG asset size is outside the 1-byte to 16-MiB preview bound: $Path"
    }

    $bytes = [IO.File]::ReadAllBytes($item.FullName)
    $signature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
    if ($bytes.Length -lt 8 -or -not [Linq.Enumerable]::SequenceEqual([byte[]]$bytes[0..7], $signature)) {
        throw "Asset is not a PNG file: $Path"
    }

    $offset = 8
    $chunkNumber = 0
    $width = 0L
    $height = 0L
    $bitDepth = 0
    $colorType = -1
    $hasIdat = $false
    $hasTransparencyChunk = $false
    $hasEnd = $false

    while ($offset -lt $bytes.Length) {
        if ($offset + 12 -gt $bytes.Length) { throw "PNG chunk header is truncated: $Path" }
        $chunkLength = Read-PreviewPngUInt32BigEndian -Bytes $bytes -Offset $offset
        if ($chunkLength -gt 16MB -or $chunkLength -gt ($bytes.Length - $offset - 12)) {
            throw "PNG chunk length is invalid or exceeds the preview bound: $Path"
        }

        $length = [int]$chunkLength
        $chunkType = [Text.Encoding]::ASCII.GetString($bytes, $offset + 4, 4)
        if ($chunkNumber -eq 0 -and ($chunkType -cne 'IHDR' -or $length -ne 13)) {
            throw "PNG must start with one 13-byte IHDR chunk: $Path"
        }
        if ($chunkType -ceq 'IHDR') {
            if ($chunkNumber -ne 0 -or $width -ne 0) { throw "PNG contains a duplicate or misplaced IHDR chunk: $Path" }
            $width = Read-PreviewPngUInt32BigEndian -Bytes $bytes -Offset ($offset + 8)
            $height = Read-PreviewPngUInt32BigEndian -Bytes $bytes -Offset ($offset + 12)
            $bitDepth = [int]$bytes[$offset + 16]
            $colorType = [int]$bytes[$offset + 17]
        }
        elseif ($chunkType -ceq 'IDAT') { $hasIdat = $true }
        elseif ($chunkType -ceq 'tRNS') { $hasTransparencyChunk = $true }

        $crcInput = [byte[]]::new(4 + $length)
        [Array]::Copy($bytes, $offset + 4, $crcInput, 0, $crcInput.Length)
        $storedCrc = Read-PreviewPngUInt32BigEndian -Bytes $bytes -Offset ($offset + 8 + $length)
        $actualCrc = Get-PreviewPngCrc32 -Bytes $crcInput
        if ($actualCrc -ne $storedCrc) { throw "PNG chunk checksum failed ($chunkType): $Path" }

        $offset += 12 + $length
        $chunkNumber++
        if ($chunkType -ceq 'IEND') {
            if ($length -ne 0 -or $offset -ne $bytes.Length) { throw "PNG IEND is malformed or followed by trailing data: $Path" }
            $hasEnd = $true
            break
        }
    }

    if ($width -lt 1 -or $height -lt 1 -or $width -gt 1024 -or $height -gt 1024 -or ($width * $height) -gt 1048576) {
        throw "PNG dimensions are outside the preview bounds: $Path"
    }
    if (-not $hasIdat -or -not $hasEnd) { throw "PNG image data or IEND is missing: $Path" }

    $validDepth = switch ($colorType) {
        0 { $bitDepth -in @(1, 2, 4, 8, 16) }
        2 { $bitDepth -in @(8, 16) }
        3 { $bitDepth -in @(1, 2, 4, 8) }
        4 { $bitDepth -in @(8, 16) }
        6 { $bitDepth -in @(8, 16) }
        default { $false }
    }
    if (-not $validDepth) { throw "PNG color type or bit depth is unsupported: $Path" }

    $hasAlphaChannel = $colorType -in @(4, 6)
    $hasTransparency = $hasAlphaChannel -or $hasTransparencyChunk
    $transparencyDescription = if ($hasAlphaChannel) { 'AlphaChannel' }
        elseif ($hasTransparencyChunk) { 'tRNS' }
        else { 'OpaqueNoAlphaOrTRNS' }

    return [pscustomobject]@{
        Width = $width
        Height = $height
        BitDepth = $bitDepth
        ColorType = $colorType
        HasAlphaChannel = $hasAlphaChannel
        HasTransparencyChunk = $hasTransparencyChunk
        HasTransparency = $hasTransparency
        TransparencyDescription = $transparencyDescription
        Bytes = $bytes.LongLength
    }
}

function Get-PreviewIconAssetSpecifications {
    return @(
        [pscustomobject]@{ SemanticRole = 'Finder'; VisualState = 'Default'; SourceRole = 'Finder'; File = 'finder.png' },
        [pscustomobject]@{ SemanticRole = 'SystemSettings'; VisualState = 'Default'; SourceRole = 'SystemSettings'; File = 'settings.png' },
        [pscustomobject]@{ SemanticRole = 'Downloads'; VisualState = 'Default'; SourceRole = 'Downloads'; File = 'downloads.png' },
        [pscustomobject]@{ SemanticRole = 'Trash'; VisualState = 'Empty'; SourceRole = 'TrashEmpty'; File = 'trash-empty.png' }
    )
}

function Assert-PreviewIconAssetSet {
    param(
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][string]$SourceRecordPath,
        [Parameter(Mandatory)][string]$AssetDirectory
    )

    foreach ($path in @($ManifestPath, $SourceRecordPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Preview icon metadata file is missing: $path" }
    }
    if (-not (Test-Path -LiteralPath $AssetDirectory -PathType Container)) {
        throw "Preview icon asset directory is missing: $AssetDirectory"
    }
    if ((Get-Item -LiteralPath $ManifestPath).Length -gt 1MB -or (Get-Item -LiteralPath $SourceRecordPath).Length -gt 1MB) {
        throw 'Preview icon manifest and source record must each be no larger than 1 MiB.'
    }

    try {
        $manifest = Get-Content -LiteralPath $ManifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        $sourceRecords = @(Get-Content -LiteralPath $SourceRecordPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop)
    }
    catch { throw "Preview icon manifest or source record is invalid JSON: $($_.Exception.Message)" }

    $specifications = @(Get-PreviewIconAssetSpecifications)
    if ($manifest.schemaVersion -ne 1 -or @($manifest.mappings).Count -ne $specifications.Count) {
        throw 'Preview icon manifest must be schema version 1 with exactly four semantic mappings.'
    }
    if ($sourceRecords.Count -ne $specifications.Count) {
        throw 'Preview icon source record must contain exactly the four expected source-role records.'
    }

    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($specification in $specifications) {
        $roleMappings = @($manifest.mappings | Where-Object { $_.selector.role -ceq $specification.SemanticRole })
        if ($roleMappings.Count -ne 1 -or @($roleMappings[0].variants).Count -ne 1) {
            throw "Expected exactly one icon mapping and variant for semantic role $($specification.SemanticRole)."
        }

        $variant = $roleMappings[0].variants[0]
        if ($variant.relativePath -cne "assets/$($specification.File)" -or
            $variant.theme -cne 'Any' -or
            $variant.state -cne $specification.VisualState -or
            $variant.redistributionStatus -cne 'LocalUseOnly') {
            throw "Invalid role/state/file combination for $($specification.SemanticRole): expected $($specification.SemanticRole) + $($specification.VisualState) -> $($specification.SourceRole)."
        }

        $records = @($sourceRecords | Where-Object { $_.File -ceq $specification.File -and $_.Role -ceq $specification.SourceRole })
        if ($records.Count -ne 1) {
            throw "Source-role mapping is invalid for $($specification.SemanticRole) + $($specification.VisualState) -> $($specification.SourceRole)."
        }
        $record = $records[0]
        if ($record.Sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Source SHA-256 is malformed for $($specification.SourceRole)." }

        $assetPath = Join-Path $AssetDirectory $specification.File
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) { throw "Preview icon image is missing: $($specification.File)" }
        $assetItem = Get-Item -LiteralPath $assetPath -ErrorAction Stop
        if (($assetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Preview icon image must be a regular local file, not a reparse point: $($specification.File)"
        }
        if ($record.Bytes -isnot [ValueType] -or [int64]$record.Bytes -ne $assetItem.Length) {
            throw "Source byte length does not match $($specification.SourceRole)."
        }

        $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256 -ErrorAction Stop).Hash
        if (-not [string]::Equals($actualHash, [string]$record.Sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Source SHA-256 does not match $($specification.SourceRole)."
        }

        $png = Read-PreviewPngMetadata -Path $assetPath
        if ([int64]$record.Width -ne $png.Width -or [int64]$record.Height -ne $png.Height -or [int64]$record.Bytes -ne $png.Bytes) {
            throw "Recorded PNG dimensions or byte length do not match $($specification.SourceRole)."
        }

        $results.Add([pscustomobject]@{
            SemanticRole = $specification.SemanticRole
            VisualState = $specification.VisualState
            SourceRole = $specification.SourceRole
            File = $specification.File
            Sha256 = $actualHash.ToLowerInvariant()
            Png = $png
        })
    }

    return @($results)
}

function Assert-PreviewAssetRootIsLocal {
    param(
        [Parameter(Mandatory)][string]$AssetRoot,
        [Parameter(Mandatory)][string]$LocalRoot
    )

    $fullAssetRoot = [IO.Path]::GetFullPath($AssetRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $fullLocalRoot = [IO.Path]::GetFullPath($LocalRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $fullLocalRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $fullAssetRoot.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Preview asset roots must remain inside the ignored D: local data directory: $fullAssetRoot"
    }
    return $fullAssetRoot
}

function Invoke-PreviewIconAssetPreflight {
    param([Parameter(Mandatory)][string]$AssetRoot)

    $root = [IO.Path]::GetFullPath($AssetRoot)
    return @(Assert-PreviewIconAssetSet `
        -ManifestPath (Join-Path $root 'manifest.json') `
        -SourceRecordPath (Join-Path $root 'source-record.json') `
        -AssetDirectory (Join-Path $root 'assets'))
}
