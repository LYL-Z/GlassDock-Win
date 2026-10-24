[CmdletBinding()]
param(
    [ValidateSet('VisualSmoke', 'ManualInteraction')]
    [string]$Mode = 'ManualInteraction',
    [switch]$ConfirmDesktopTest,
    [string]$AuthorizationPhrase,
    [switch]$PreflightOnly,
    [string]$AssetRoot
)

$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:LocalRoot = Join-Path $script:RepoRoot '.local'
. (Join-Path $PSScriptRoot 'preview-icon-asset-preflight.ps1')

$privateIconRoot = Join-Path $script:LocalRoot 'experimental-dock\icon-assets'
if ([string]::IsNullOrWhiteSpace($AssetRoot)) { $AssetRoot = $privateIconRoot }
elseif (-not $PreflightOnly) { throw '-AssetRoot is available only with -PreflightOnly; desktop tests always use the private local icon set.' }
$AssetRoot = Assert-PreviewAssetRootIsLocal -AssetRoot $AssetRoot -LocalRoot $script:LocalRoot

if ($PreflightOnly) {
    $preflightResult = @(Invoke-PreviewIconAssetPreflight -AssetRoot $AssetRoot)
    $preflightResult | ConvertTo-Json -Depth 8 -Compress
    return
}

. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

$timeoutSeconds = if ($Mode -eq 'VisualSmoke') { 90 } else { 300 }
$requiredPhrase = if ($Mode -eq 'VisualSmoke') {
    'RUN GLASSDOCK VISUAL SMOKE TEST'
} else {
    'RUN GLASSDOCK MANUAL INTERACTION TEST'
}
$purpose = if ($Mode -eq 'VisualSmoke') {
    'single automatic PreviewOnly WPF Dock visual smoke, window-only WGC capture, and normal run-owned close; no user input'
} else {
    'manual PreviewOnly WPF Dock pin, reorder, remove-reference, reload, and close test; no synthetic input'
}
Confirm-M0InteractiveTest -Confirmed:$ConfirmDesktopTest `
    -Purpose $purpose `
    -AuthorizationPhrase $AuthorizationPhrase -RequiredPhrase $requiredPhrase

$appExe = Join-Path $script:RepoRoot 'src\GlassDock.Experimental.Wpf\bin\Release\net10.0-windows10.0.19041.0\GlassDock.Experimental.Wpf.exe'
$watchdogExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
$wgcExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.WgcProbe\bin\Release\net10.0-windows10.0.26100.0\WgcProbe.exe'
foreach ($path in @($appExe, $watchdogExe, $wgcExe)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required local test executable is missing: $path" }
}

$existing = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    try { [string]::Equals($_.Path, $appExe, [StringComparison]::OrdinalIgnoreCase) }
    catch { $false }
})
if ($existing.Count -gt 0) {
    throw 'An experimental Dock process is already running. The test will not attach to or stop an existing process.'
}

$privateManifest = Join-Path $privateIconRoot 'manifest.json'
$privateSourceRecord = Join-Path $privateIconRoot 'source-record.json'
$privateAssetDirectory = Join-Path $privateIconRoot 'assets'
$preflightResult = @(Invoke-PreviewIconAssetPreflight -AssetRoot $privateIconRoot)
$expectedMappings = @($preflightResult | ForEach-Object {
    [pscustomobject]@{ Role = $_.SemanticRole; File = $_.File; State = $_.VisualState; SourceRole = $_.SourceRole }
})
Write-Output ('Preflight passed for role/state/source mappings: ' + (($preflightResult | ForEach-Object {
    '{0}+{1}->{2}; PNG {3}x{4}, color type {5}, bit depth {6}, alpha={7}, transparency={8}' -f `
        $_.SemanticRole, $_.VisualState, $_.SourceRole, $_.Png.Width, $_.Png.Height, $_.Png.ColorType, `
        $_.Png.BitDepth, $_.Png.HasAlphaChannel, $_.Png.TransparencyDescription
}) -join ' | '))

$runId = [Guid]::NewGuid().ToString('N')
$evidenceRoot = Join-Path $script:LocalRoot "evidence\experimental-preview\$runId"
$profileRoot = Join-Path $evidenceRoot 'profile'
$profileIconRoot = Join-Path $profileRoot 'icon-assets'
$profileAssetDirectory = Join-Path $profileIconRoot 'assets'
$diagnosticsPath = Join-Path $evidenceRoot 'dock-diagnostics.jsonl'
$windowReadinessPath = Join-Path $evidenceRoot 'window-readiness.jsonl'
$firstCapture = Join-Path $evidenceRoot 'dock-first-view.png'
$reloadCapture = Join-Path $evidenceRoot 'dock-reloaded-view.png'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($timeoutSeconds - 10)
$windowReadyTimeoutSeconds = if ($Mode -eq 'VisualSmoke') { 20 } else { 45 }
$iconReadyTimeoutSeconds = if ($Mode -eq 'VisualSmoke') { 25 } else { 60 }
$session = $null
$dockProcess = $null
$completed = $false

if (Test-Path -LiteralPath $evidenceRoot) {
    throw 'The unique D: evidence directory already exists; refusing to reuse preview state.'
}
New-Item -ItemType Directory -Path $profileAssetDirectory -Force | Out-Null
Copy-Item -LiteralPath $privateManifest -Destination (Join-Path $profileIconRoot 'manifest.json')
foreach ($expected in $expectedMappings) {
    Copy-Item -LiteralPath (Join-Path $privateAssetDirectory $expected.File) -Destination (Join-Path $profileAssetDirectory $expected.File)
}
Copy-Item -LiteralPath $privateSourceRecord -Destination (Join-Path $evidenceRoot 'icon-source-record.json')

function Get-PreviewEvents {
    param([int]$ProcessId = 0)

    if (-not (Test-Path -LiteralPath $diagnosticsPath -PathType Leaf)) { return @() }
    $events = @(Get-Content -LiteralPath $diagnosticsPath -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_ | ConvertFrom-Json -ErrorAction Stop } catch { $null }
    } | Where-Object { $null -ne $_ })
    if ($ProcessId -gt 0) {
        return @($events | Where-Object { $_.ProcessId -eq $ProcessId })
    }
    return $events
}

if (-not ('GlassDock.Windows.Windowing.DockWindowReadinessPolicy' -as [type])) {
    $windowAssemblyRoot = Split-Path -Parent $appExe
    foreach ($assemblyName in @('GlassDock.Core.dll', 'GlassDock.App.dll', 'GlassDock.Windows.dll')) {
        $assemblyPath = Join-Path $windowAssemblyRoot $assemblyName
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "The built HWND readiness policy assembly is missing: $assemblyPath"
        }
        Add-Type -Path $assemblyPath -ErrorAction Stop
    }
}

function Write-PreviewWindowReadinessRecord {
    param([Parameter(Mandatory)][string]$Event, [Parameter(Mandatory)][object]$Detail)

    $record = [pscustomobject]@{
        TimestampUtc = [DateTimeOffset]::UtcNow
        Event = $Event
        Detail = $Detail
    } | ConvertTo-Json -Depth 8 -Compress
    [IO.File]::AppendAllText($windowReadinessPath, $record + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Wait-PreviewWindowReady {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process, [Parameter(Mandatory)][string]$Phase)

    $milestoneDeadline = [DateTimeOffset]::UtcNow.AddSeconds($windowReadyTimeoutSeconds)
    $nativeVisibilityWaitStarted = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Assert-M0InteractiveTestCanContinue -Session $session
        $Process.Refresh()
        if ($Process.HasExited) { throw "The PreviewOnly Dock exited during $Phase startup (exit $($Process.ExitCode))." }
        $events = Get-PreviewEvents -ProcessId $Process.Id
        $catalogEvent = @($events | Where-Object Event -eq 'catalog-load-completed' | Select-Object -Last 1)
        $hwndCreated = @($events | Where-Object Event -eq 'window-hwnd-created' | Select-Object -Last 1)
        $visibleEvent = @($events | Where-Object Event -eq 'window-visible' | Select-Object -Last 1)
        $layoutEvent = @($events | Where-Object Event -eq 'window-layout-prepared' | Select-Object -Last 1)
        $contentEvent = @($events | Where-Object Event -eq 'window-content-rendered' | Select-Object -Last 1)
        if ($hwndCreated.Count -gt 0 -and $visibleEvent.Count -gt 0 -and $layoutEvent.Count -gt 0 -and $contentEvent.Count -gt 0) {
            $reportedHwnd = [string]$hwndCreated[0].Detail.Hwnd
            if ($reportedHwnd -notmatch '^0x[0-9A-Fa-f]+$') {
                throw "WPF SourceInitialized reported an invalid HWND value during ${Phase}: '$reportedHwnd'."
            }
            $reportedHandle = [Convert]::ToInt64($reportedHwnd.Substring(2), 16)
            $windows = @([GlassDock.Windows.Windowing.DockTopLevelWindowEnumerator]::Enumerate())
            $reportedRows = @($windows | Where-Object Handle -eq $reportedHandle)
            if ($reportedRows.Count -eq 0) {
                throw "WPF HWND $reportedHwnd was not found by Win32 EnumWindows during $Phase. No other window will be substituted."
            }
            if ($reportedRows.Count -ne 1) {
                throw "Win32 enumeration returned $($reportedRows.Count) observations for WPF HWND $reportedHwnd during $Phase; expected exactly one."
            }
            $reported = $reportedRows[0]
            if (-not $reported.IsWindow) { throw "Reported HWND $reportedHwnd is no longer a valid Win32 window during $Phase." }
            if ($reported.ProcessId -ne $Process.Id) {
                throw "Reported HWND $reportedHwnd belongs to PID $($reported.ProcessId), expected run-owned Dock PID $($Process.Id)."
            }
            if (-not $reported.IsVisible -or $reported.Width -le 0 -or $reported.Height -le 0) {
                if ($null -eq $nativeVisibilityWaitStarted) { $nativeVisibilityWaitStarted = [DateTimeOffset]::UtcNow }
                if ([DateTimeOffset]::UtcNow - $nativeVisibilityWaitStarted -gt [TimeSpan]::FromSeconds(2)) {
                    $detail = [pscustomobject]@{
                        Hwnd = $reportedHwnd; ProcessId = $reported.ProcessId; IsWindow = $reported.IsWindow
                        IsVisible = $reported.IsVisible; Left = $reported.Left; Top = $reported.Top
                        Right = $reported.Right; Bottom = $reported.Bottom; Width = $reported.Width; Height = $reported.Height
                    }
                    Write-PreviewWindowReadinessRecord -Event 'window-readiness-rejected' -Detail $detail
                    throw "Run-owned HWND $reportedHwnd did not become visible with positive bounds during ${Phase}: $($detail | ConvertTo-Json -Compress)."
                }
                Start-Sleep -Milliseconds 100
                continue
            }

            $visibleCandidates = @($windows | Where-Object {
                $_.ProcessId -eq $Process.Id -and $_.IsWindow -and $_.IsVisible -and $_.Width -gt 0 -and $_.Height -gt 0
            })
            if ($visibleCandidates.Count -eq 0) {
                throw "Win32 enumeration found zero visible, non-zero-size candidate windows for Dock PID $($Process.Id) during $Phase."
            }
            if ($visibleCandidates.Count -gt 1) {
                $summary = @($visibleCandidates | ForEach-Object {
                    [pscustomobject]@{ Hwnd = ('0x{0:X}' -f $_.Handle); Left = $_.Left; Top = $_.Top; Width = $_.Width; Height = $_.Height }
                })
                Write-PreviewWindowReadinessRecord -Event 'window-readiness-ambiguous' -Detail $summary
                throw "Win32 enumeration found $($visibleCandidates.Count) visible candidate windows for Dock PID $($Process.Id) during $Phase; refusing an ambiguous WGC target: $($summary | ConvertTo-Json -Compress)."
            }
            if ($visibleCandidates[0].Handle -ne $reportedHandle) {
                throw "The unique visible Dock candidate does not match WPF SourceInitialized HWND $reportedHwnd during $Phase."
            }
            $readinessObservations = [System.Collections.Generic.List[GlassDock.Windows.Windowing.DockWindowObservation]]::new()
            foreach ($window in $windows) { $readinessObservations.Add($window) }
            $readiness = [GlassDock.Windows.Windowing.DockWindowReadinessPolicy]::Evaluate(
                $readinessObservations, $Process.Id, [IntPtr]$reportedHandle,
                $layoutEvent.Count -gt 0, $contentEvent.Count -gt 0)
            if (-not $readiness.IsReady) {
                throw "The unit-tested HWND readiness policy rejected the candidate during ${Phase}: $($readiness.DiagnosticCode)."
            }
            if ($hwndCreated[0].Detail.ProcessId -ne $Process.Id) {
                throw "WPF SourceInitialized reported PID $($hwndCreated[0].Detail.ProcessId), expected the run-owned Dock PID $($Process.Id)."
            }
            if ($layoutEvent[0].Detail.VisualLayoutVerified -ne $true) {
                throw "WPF measured layout does not match the geometry plan during ${Phase}; refusing to capture a potentially clipped Dock."
            }

            $detail = [pscustomobject]@{
                Hwnd = $reportedHwnd
                ProcessId = $Process.Id
                IsWindow = $reported.IsWindow
                IsVisible = $reported.IsVisible
                Bounds = [pscustomobject]@{ Left = $reported.Left; Top = $reported.Top; Right = $reported.Right; Bottom = $reported.Bottom }
                ReadinessStatus = $readiness.Status.ToString()
                VisibleCandidateCount = $readiness.VisibleCandidateCount
                LayoutPreparedMilliseconds = $layoutEvent[0].ElapsedMilliseconds
                ContentRenderedMilliseconds = $contentEvent[0].ElapsedMilliseconds
                WgcWindowCaptureEligible = $true
                CatalogStatus = if ($catalogEvent.Count -gt 0) { $catalogEvent[0].Detail.Status } else { 'StillLoading' }
                MainWindowHandleWasUsed = $false
            }
            Write-PreviewWindowReadinessRecord -Event 'window-readiness-passed' -Detail $detail
            return [pscustomobject]@{
                WindowHandle = [IntPtr]$reportedHandle
                Hwnd = $reportedHwnd
                VisibleMilliseconds = $visibleEvent[0].ElapsedMilliseconds
                LayoutPreparedMilliseconds = $layoutEvent[0].ElapsedMilliseconds
                ContentRenderedMilliseconds = $contentEvent[0].ElapsedMilliseconds
                ProcessStartUtc = $Process.StartTime.ToUniversalTime().ToString('O')
                ProcessToWgcEligibleMilliseconds = [Math]::Round(([DateTimeOffset]::UtcNow - $Process.StartTime.ToUniversalTime()).TotalMilliseconds, 3)
                CatalogLoadMilliseconds = if ($catalogEvent.Count -gt 0) { $catalogEvent[0].ElapsedMilliseconds } else { $null }
                DpiScaleX = $layoutEvent[0].Detail.DpiScaleX
                DpiScaleY = $layoutEvent[0].Detail.DpiScaleY
                DockWidthDip = $layoutEvent[0].Detail.WidthDip
                DockHeightDip = $layoutEvent[0].Detail.HeightDip
                IconSizeDip = $layoutEvent[0].Detail.IconSizeDip
                IconPixelsAtWindowDpiX = $layoutEvent[0].Detail.IconPixelsAtWindowDpiX
                IconPixelsAtWindowDpiY = $layoutEvent[0].Detail.IconPixelsAtWindowDpiY
            }
        }
        if ([DateTimeOffset]::UtcNow -gt $milestoneDeadline) {
            throw "The PreviewOnly Dock did not emit separate HWND, visibility, layout, and first-render milestones within $windowReadyTimeoutSeconds seconds during $Phase."
        }
        Start-Sleep -Milliseconds 150
    }
    throw "The PreviewOnly Dock did not expose a loaded window before the shared 5-minute deadline during $Phase."
}

function Capture-PreviewWindow {
    param([Parameter(Mandatory)][IntPtr]$WindowHandle, [Parameter(Mandatory)][string]$OutputPath)

    $hwndText = '0x{0:X}' -f $WindowHandle.ToInt64()
    $captureProcess = Start-M0OwnedTestProcess -Session $session -FilePath $wgcExe `
        -ArgumentList @('capture-window', $hwndText, $OutputPath) -WorkingDirectory (Split-Path -Parent $wgcExe)
    try {
        $remaining = [Math]::Max(1, [int]($deadline - [DateTimeOffset]::UtcNow).TotalMilliseconds)
        if (-not $captureProcess.WaitForExit([Math]::Min(15000, $remaining))) {
            throw 'The window-only WGC capture exceeded its 15-second limit.'
        }
        if ($captureProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
            throw "Window-only WGC capture failed with exit code $($captureProcess.ExitCode)."
        }
        Write-PreviewWindowReadinessRecord -Event 'wgc-window-capture-completed' -Detail ([pscustomobject]@{
            Hwnd = $hwndText
            PngFileName = Split-Path -Leaf $OutputPath
            ProcessId = $dockProcess.Id
        })
        Write-Output "WGC window-only capture saved under D: .local evidence: $OutputPath"
    }
    finally {
        if ($captureProcess) {
            $captureProcess.Refresh()
            if (-not $captureProcess.HasExited) { [void](Stop-M0OwnedTestProcess -Session $session -Process $captureProcess) }
            else { [void](Stop-M0OwnedTestProcess -Session $session -Process $captureProcess) }
        }
    }
}

function Start-PreviewDock {
    $args = @('--preview-only', '--preview-native-role-icons', '--profile-dir', $profileRoot, '--diagnostics-path', $diagnosticsPath)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-M0OwnedTestProcess -Session $session -FilePath $appExe -ArgumentList $args `
        -WorkingDirectory (Split-Path -Parent $appExe)
    $process.Refresh()
    $startup = Wait-PreviewWindowReady -Process $process -Phase 'Dock preview'
    Assert-PreviewContentReady -Process $process
    Assert-PreviewGeometryReady -Process $process
    $startup | Add-Member -NotePropertyName LauncherStartToReadyMilliseconds -NotePropertyValue ([Math]::Round($timer.Elapsed.TotalMilliseconds, 3))
    return [pscustomobject]@{ Process = $process; Startup = $startup }
}

function Assert-PreviewContentReady {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)

    $events = Get-PreviewEvents -ProcessId $Process.Id
    $manifestEvent = @($events | Where-Object Event -eq 'icon-manifest-loaded' | Select-Object -Last 1)
    if ($manifestEvent.Count -lt 1 -or $manifestEvent[0].Detail.Status -ne 'Loaded' -or $manifestEvent[0].Detail.MappingCount -ne 4) {
        throw 'The four-role private icon manifest did not load. Stop without continuing the preview.'
    }
    $seedEvent = @($events | Where-Object Event -eq 'native-role-preview-seeded' | Select-Object -Last 1)
    if ($seedEvent.Count -lt 1 -or $seedEvent[0].Detail.PinnedCount -lt 2 -or $seedEvent[0].Detail.FileCount -lt 2) {
        throw 'The isolated profile does not contain Finder, Settings, Downloads, and Trash preview items.'
    }
}

function Assert-PreviewGeometryReady {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)

    $events = Get-PreviewEvents -ProcessId $Process.Id
    $matches = @($events | Where-Object Event -eq 'dock-visual-geometry' | Select-Object -Last 1)
    if ($matches.Count -ne 1) { throw 'Dock visual geometry diagnostics are missing for the run-owned process.' }
    $geometry = $matches[0].Detail
    if ($geometry.Edge -ne 'Bottom' -or $geometry.IconSizeDip -ne 48 -or $geometry.IconCount -lt 4) {
        throw "Expected a 48-DIP bottom Dock with at least four role tiles; observed edge=$($geometry.Edge), size=$($geometry.IconSizeDip), items=$($geometry.IconCount)."
    }
    $plate = $geometry.BackplateBounds
    foreach ($icon in @($geometry.IconBounds)) {
        if ($icon.Width -ne 48 -or $icon.Height -ne 48 -or
            $icon.X -lt $plate.X -or $icon.Y -lt $plate.Y -or
            ($icon.X + $icon.Width) -gt ($plate.X + $plate.Width) + 0.01 -or
            ($icon.Y + $icon.Height) -gt ($plate.Y + $plate.Height) + 0.01) {
            throw "A Dock icon bound escapes the measured backplate: $(($icon | ConvertTo-Json -Compress))."
        }
    }
    if ($geometry.BackplateWidthDip -le 0 -or $geometry.BackplateHeightDip -le 0 -or $geometry.IsScrollable) {
        throw 'The role preview geometry is empty or unexpectedly clipped by a scroll viewport.'
    }
}

function Wait-PreviewRoleIconsReady {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process, [Parameter(Mandatory)][string]$Phase)

    $iconDeadline = [DateTimeOffset]::UtcNow.AddSeconds($iconReadyTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline -and [DateTimeOffset]::UtcNow -lt $iconDeadline) {
        Assert-M0InteractiveTestCanContinue -Session $session
        $Process.Refresh()
        if ($Process.HasExited) { throw "The PreviewOnly Dock exited while icon assets were loading during $Phase." }
        $events = Get-PreviewEvents -ProcessId $Process.Id
        $resolved = @($events | Where-Object Event -eq 'dock-icon-resolved')
        $latestByRole = @($resolved | Group-Object { $_.Detail.Role } | ForEach-Object { $_.Group | Select-Object -Last 1 })
        $failures = @($latestByRole | Where-Object { -not $_.Detail.Decoded })
        if ($failures.Count -gt 0) {
            throw "A required native-role icon failed to decode during ${Phase}: $(($failures | ForEach-Object { $_.Detail.Role + ':' + $_.Detail.DiagnosticCode }) -join ', ')."
        }
        $requiredRoles = @('Finder', 'SystemSettings', 'Downloads', 'Trash')
        $complete = @($requiredRoles | Where-Object {
            $role = $_
            @($resolved | Where-Object { $_.Detail.Role -eq $role -and $_.Detail.Decoded }).Count -gt 0
        })
        if ($complete.Count -eq 4) { return }
        Start-Sleep -Milliseconds 150
    }
    throw "The four role icons did not decode within the independent $iconReadyTimeoutSeconds-second icon-load window during $Phase; the visible Dock is not treated as visually ready."
}

function Assert-PreviewIconMapping {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process, [Parameter(Mandatory)][string]$Phase)

    $events = Get-PreviewEvents -ProcessId $Process.Id
    $resolved = @($events | Where-Object Event -eq 'dock-icon-resolved')
    foreach ($role in @('Finder', 'SystemSettings', 'Downloads', 'Trash')) {
        $matches = @($resolved | Where-Object { $_.Detail.Role -eq $role } | Select-Object -Last 1)
        if ($matches.Count -lt 1 -or -not $matches[0].Detail.Decoded) {
            throw "The $role preview icon did not decode during $Phase. Stop without continuing the preview."
        }
        if ($role -ne 'Trash' -and $matches[0].Detail.Source -ne 'UserRoleOverride') {
            throw "The user-provided $role icon was not selected during $Phase. Stop without continuing the preview."
        }
        if ($role -eq 'Trash') {
            $visualState = $matches[0].Detail.VisualState
            $source = $matches[0].Detail.Source
            if ($visualState -eq 'Empty' -and $source -ne 'UserRoleOverride') {
                throw "The empty Recycle Bin did not select the supplied empty Trash asset during $Phase."
            }
            if ($visualState -ne 'Empty' -and $source -eq 'UserRoleOverride') {
                throw "The empty Trash asset was selected while the Recycle Bin was full or unknown during $Phase."
            }
        }
    }
}

function Wait-PreviewDockExit {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process, [Parameter(Mandatory)][string]$Phase)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Assert-M0InteractiveTestCanContinue -Session $session
        $Process.Refresh()
        if ($Process.HasExited) { break }
        Start-Sleep -Milliseconds 200
    }
    $Process.Refresh()
    if (-not $Process.HasExited) {
        Write-PreviewWindowReadinessRecord -Event 'manual-interaction-timeout' -Detail ([pscustomobject]@{
            ProcessId = $Process.Id
            Phase = $Phase
            Result = 'NOT_TESTED'
            Reason = 'No manual close was observed before the bounded interaction window ended.'
        })
        return $false
    }
    $stopped = Stop-M0OwnedTestProcess -Session $session -Process $Process
    if (-not $stopped.Stopped) { throw "The $Phase Dock process did not stop cleanly." }
    return $true
}

function Close-PreviewDockWindow {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [Parameter(Mandatory)][IntPtr]$WindowHandle,
        [Parameter(Mandatory)][string]$Phase
    )

    $matchingWindows = @([GlassDock.Windows.Windowing.DockTopLevelWindowEnumerator]::Enumerate() | Where-Object {
        $_.Handle -eq $WindowHandle.ToInt64() -and $_.ProcessId -eq $Process.Id -and $_.IsWindow -and $_.IsVisible
    })
    if ($matchingWindows.Count -ne 1) {
        throw "Normal close refused for ${Phase}: HWND did not resolve to exactly one visible window owned by PID $($Process.Id)."
    }
    [void][GlassDockM0SafetyNative]::PostMessage($WindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    $Process.Refresh()
    if (-not $Process.WaitForExit(3000)) {
        $fallback = Stop-M0OwnedTestProcess -Session $session -Process $Process
        if (-not $fallback.Stopped) { throw "The run-owned $Phase Dock did not close after a WM_CLOSE request." }
        Write-PreviewWindowReadinessRecord -Event 'window-normal-close-fallback' -Detail ([pscustomobject]@{
            ProcessId = $Process.Id
            Hwnd = '0x{0:X}' -f $WindowHandle.ToInt64()
            StopMethod = $fallback.Method
        })
        return $fallback
    }
    $stopped = Stop-M0OwnedTestProcess -Session $session -Process $Process
    if (-not $stopped.Stopped) { throw "The $Phase Dock process did not stop cleanly after WM_CLOSE." }
    Write-PreviewWindowReadinessRecord -Event 'window-normal-close' -Detail ([pscustomobject]@{
        ProcessId = $Process.Id
        Hwnd = '0x{0:X}' -f $WindowHandle.ToInt64()
        Method = 'WM_CLOSE'
    })
    return $stopped
}

try {
    $session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
        -Purpose $purpose -WatchdogExe $watchdogExe -TimeoutSeconds $timeoutSeconds

    # Do not force focus back to the pre-test application: preserve any user focus changes.
    $session.Manifest.ForegroundWindowHandle = 0
    $session.Manifest.ForegroundProcessId = 0
    Write-M0SafetyManifest -Session $session

    $first = Start-PreviewDock
    $dockProcess = $first.Process
    Wait-PreviewRoleIconsReady -Process $dockProcess -Phase 'first view'
    Assert-PreviewIconMapping -Process $dockProcess -Phase 'first view'
    Capture-PreviewWindow -WindowHandle $first.Startup.WindowHandle -OutputPath $firstCapture
    Write-Output ("First view timing and DPI: " + ($first.Startup | ConvertTo-Json -Compress -Depth 4))

    if ($Mode -eq 'VisualSmoke') {
        [void](Close-PreviewDockWindow -Process $dockProcess -WindowHandle $first.Startup.WindowHandle -Phase 'visual-smoke')
        $dockProcess = $null
        $manualStatus = 'NOT_TESTED · no manual interactions are part of visual smoke'
        $completed = $true
    }
    else {
        Write-Output 'Manual steps (up to 5 minutes): 1) use the Dock right-click menu > Add Applications to pin two apps; 2) drag one pinned tile to reorder; 3) right-click a newly added tile and choose Remove from Dock; 4) close this Dock with the menu Exit Preview or Alt+F4. PreviewOnly blocks all app/file execution. The runner will classify unperformed actions as NOT_TESTED, not as a Dock failure.'
        $closedByUser = Wait-PreviewDockExit -Process $dockProcess -Phase 'manual-first-view'
        if ($closedByUser) {
            $dockProcess = $null
            $firstRunEvents = Get-PreviewEvents -ProcessId $first.Process.Id
            $mutations = @($firstRunEvents | Where-Object Event -eq 'dock-state-mutation')
            $successfulPins = @($mutations | Where-Object {
                $_.Detail.Operation -eq 'PinApplication' -and $_.Detail.Succeeded -and $_.Detail.Changed
            })
            $successfulReorders = @($mutations | Where-Object {
                $_.Detail.Change -eq 'Reordered' -and $_.Detail.Succeeded -and $_.Detail.Changed
            })
            $successfulRemovals = @($mutations | Where-Object {
                $_.Detail.Change -eq 'Removed' -and $_.Detail.Succeeded -and $_.Detail.Changed
            })
            $expectedActionsCompleted = $successfulPins.Count -ge 2 -and $successfulReorders.Count -ge 1 `
                -and $successfulRemovals.Count -ge 1 -and $successfulRemovals[-1].Detail.PinnedCount -ge 2
            if (-not $expectedActionsCompleted) {
                $failedAttempt = @($mutations | Where-Object {
                    -not $_.Detail.Succeeded -and ($_.Detail.Operation -eq 'PinApplication' -or $_.Detail.Change -in @('Reordered', 'Removed'))
                }).Count -gt 0
                $manualStatus = if ($failedAttempt) { 'FAIL · an attempted state change was rejected' } else { 'NOT_TESTED · one or more requested manual actions were not recorded' }
                Write-Output $manualStatus
            }
            else {
                $expectedPinnedHash = $successfulRemovals[-1].Detail.PinnedOrderSha256
                if ([string]::IsNullOrWhiteSpace($expectedPinnedHash)) { throw 'The final saved Dock order fingerprint is missing.' }
                $reload = Start-PreviewDock
                $dockProcess = $reload.Process
                Wait-PreviewRoleIconsReady -Process $dockProcess -Phase 'reloaded view'
                Assert-PreviewIconMapping -Process $dockProcess -Phase 'reloaded view'
                Capture-PreviewWindow -WindowHandle $reload.Startup.WindowHandle -OutputPath $reloadCapture
                $reloadEvents = Get-PreviewEvents -ProcessId $reload.Process.Id
                $loadedConfiguration = @($reloadEvents | Where-Object {
                    $_.Event -eq 'configuration-loaded' -and $_.Detail.PinnedOrderSha256 -eq $expectedPinnedHash
                })
                if ($loadedConfiguration.Count -lt 1) { throw 'The reopened Dock configuration did not match the saved pinned-item IDs/order fingerprint.' }
                [void](Close-PreviewDockWindow -Process $dockProcess -WindowHandle $reload.Startup.WindowHandle -Phase 'manual-reload')
                $dockProcess = $null
                $manualStatus = 'PASS · pin, reorder, remove-reference, and reload persistence were observed'
            }
        }
        else {
            $dockProcess.Refresh()
            if (-not $dockProcess.HasExited) { [void](Stop-M0OwnedTestProcess -Session $session -Process $dockProcess) }
            $dockProcess = $null
            $manualStatus = 'NOT_TESTED · manual window close was not observed before timeout; owned-process cleanup was used'
            Write-Output $manualStatus
        }
        $completed = $manualStatus.StartsWith('PASS', [StringComparison]::Ordinal)
    }
}
finally {
    if ($session) {
        if ($dockProcess) {
            $dockProcess.Refresh()
            if (-not $dockProcess.HasExited) { [void](Stop-M0OwnedTestProcess -Session $session -Process $dockProcess) }
            else { [void](Stop-M0OwnedTestProcess -Session $session -Process $dockProcess) }
        }
        Restore-M0InteractiveTestEnvironment -Session $session
        $restoreSummary = Stop-M0InteractiveTestSession -Session $session -Completed $completed
        if (-not $restoreSummary.RestorePassed) { throw 'Desktop recovery checks failed. Do not rerun; inspect the saved post-state.json first.' }
    }
}

$runProcessIds = @($first.Process.Id)
if ($reload) { $runProcessIds += $reload.Process.Id }
$finalEvents = @(Get-PreviewEvents | Where-Object { $_.ProcessId -in $runProcessIds })
$latestRoleEvents = @($finalEvents | Where-Object Event -eq 'dock-icon-resolved' | Group-Object { $_.Detail.Role } | ForEach-Object { $_.Group | Select-Object -Last 1 })
$iconSummary = @($latestRoleEvents | ForEach-Object {
    [pscustomobject]@{
        Role = $_.Detail.Role
        VisualState = $_.Detail.VisualState
        Source = $_.Detail.Source
        Status = $_.Detail.Status
        Decoded = $_.Detail.Decoded
    }
})
$previewSummary = [ordered]@{
    RunId = $runId
    Completed = $completed
    Mode = $Mode
    PreviewOnly = $true
    NativeRoleItems = @('Finder', 'SystemSettings', 'Downloads', 'Trash')
    FirstView = $first.Startup
    ReloadView = $reload.Startup
    RoleIcons = $iconSummary
    FirstScreenshot = if (Test-Path -LiteralPath $firstCapture) { Split-Path -Leaf $firstCapture } else { $null }
    ReloadScreenshot = if (Test-Path -LiteralPath $reloadCapture) { Split-Path -Leaf $reloadCapture } else { $null }
    ConfigurationOrderHash = $expectedPinnedHash
    ManualInteractionStatus = $manualStatus
    RestorationPassed = $restoreSummary.RestorePassed
    RestorationEvidencePath = $restoreSummary.RunId
}
[IO.File]::WriteAllText((Join-Path $evidenceRoot 'preview-summary.json'), ($previewSummary | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "Preview test completed. Evidence: $evidenceRoot"
if ($Mode -eq 'ManualInteraction' -and $manualStatus.StartsWith('FAIL', [StringComparison]::Ordinal)) {
    throw "A requested manual Dock interaction failed. See the local D: evidence summary: $evidenceRoot"
}
