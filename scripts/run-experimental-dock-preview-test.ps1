[CmdletBinding()]
param(
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

$requiredPhrase = 'RUN GLASSDOCK PREVIEW TEST'
Confirm-M0InteractiveTest -Confirmed:$ConfirmDesktopTest `
    -Purpose 'two bounded PreviewOnly WPF Dock views, manual pin/reorder/remove actions, and window-only WGC captures' `
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
$firstCapture = Join-Path $evidenceRoot 'dock-first-view.png'
$reloadCapture = Join-Path $evidenceRoot 'dock-reloaded-view.png'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(300)
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
    if (-not (Test-Path -LiteralPath $diagnosticsPath -PathType Leaf)) { return @() }
    return @(Get-Content -LiteralPath $diagnosticsPath -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_ | ConvertFrom-Json -ErrorAction Stop } catch { $null }
    } | Where-Object { $null -ne $_ })
}

function Wait-PreviewWindowReady {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process, [Parameter(Mandatory)][string]$Phase)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Assert-M0InteractiveTestCanContinue -Session $session
        $Process.Refresh()
        if ($Process.HasExited) { throw "The PreviewOnly Dock exited during $Phase startup (exit $($Process.ExitCode))." }
        $events = Get-PreviewEvents
        $catalogEvent = @($events | Where-Object Event -eq 'catalog-load-completed' | Select-Object -Last 1)
        $windowEvent = @($events | Where-Object Event -eq 'window-loaded' | Select-Object -Last 1)
        if ($catalogEvent.Count -gt 0 -and $windowEvent.Count -gt 0 -and $Process.MainWindowHandle -ne [IntPtr]::Zero) {
            if ($catalogEvent[0].Detail.Status -ne 'Succeeded') {
                throw "The application catalog did not load successfully in $($Phase): $($catalogEvent[0].Detail.Status)."
            }
            return [pscustomobject]@{
                WindowHandle = $Process.MainWindowHandle
                ProcessStartUtc = $Process.StartTime.ToUniversalTime().ToString('O')
                ProcessToWindowReadyMilliseconds = [Math]::Round(([DateTimeOffset]::UtcNow - $Process.StartTime.ToUniversalTime()).TotalMilliseconds, 3)
                CatalogLoadMilliseconds = $catalogEvent[0].ElapsedMilliseconds
                WindowLoadedMilliseconds = $windowEvent[0].ElapsedMilliseconds
                DpiScaleX = $windowEvent[0].Detail.DpiScaleX
                DpiScaleY = $windowEvent[0].Detail.DpiScaleY
                DockWidthDip = $windowEvent[0].Detail.WidthDip
                DockHeightDip = $windowEvent[0].Detail.HeightDip
                IconSizeDip = $windowEvent[0].Detail.IconSizeDip
                IconPixelsAtWindowDpiX = $windowEvent[0].Detail.IconPixelsAtWindowDpiX
                IconPixelsAtWindowDpiY = $windowEvent[0].Detail.IconPixelsAtWindowDpiY
            }
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
    Assert-PreviewContentReady
    $startup | Add-Member -NotePropertyName LauncherStartToReadyMilliseconds -NotePropertyValue ([Math]::Round($timer.Elapsed.TotalMilliseconds, 3))
    return [pscustomobject]@{ Process = $process; Startup = $startup }
}

function Assert-PreviewContentReady {
    $events = Get-PreviewEvents
    $manifestEvent = @($events | Where-Object Event -eq 'icon-manifest-loaded' | Select-Object -Last 1)
    if ($manifestEvent.Count -lt 1 -or $manifestEvent[0].Detail.Status -ne 'Loaded' -or $manifestEvent[0].Detail.MappingCount -ne 4) {
        throw 'The four-role private icon manifest did not load. Stop without continuing the preview.'
    }
    $seedEvent = @($events | Where-Object Event -eq 'native-role-preview-seeded' | Select-Object -Last 1)
    if ($seedEvent.Count -lt 1 -or $seedEvent[0].Detail.PinnedCount -lt 2 -or $seedEvent[0].Detail.FileCount -lt 2) {
        throw 'The isolated profile does not contain Finder, Settings, Downloads, and Trash preview items.'
    }

    $resolved = @($events | Where-Object Event -eq 'dock-icon-resolved')
    foreach ($role in @('Finder', 'SystemSettings', 'Downloads', 'Trash')) {
        $matches = @($resolved | Where-Object { $_.Detail.Role -eq $role } | Select-Object -Last 1)
        if ($matches.Count -lt 1 -or -not $matches[0].Detail.Decoded) {
            throw "The $role preview icon did not decode. Stop without continuing the preview."
        }
        if ($role -ne 'Trash' -and $matches[0].Detail.Source -ne 'UserRoleOverride') {
            throw "The user-provided $role icon was not selected. Stop without continuing the preview."
        }
        if ($role -eq 'Trash') {
            $visualState = $matches[0].Detail.VisualState
            $source = $matches[0].Detail.Source
            if ($visualState -eq 'Empty' -and $source -ne 'UserRoleOverride') {
                throw 'The empty Recycle Bin did not select the supplied empty Trash asset.'
            }
            if ($visualState -ne 'Empty' -and $source -eq 'UserRoleOverride') {
                throw 'The empty Trash asset was selected while the Recycle Bin was full or unknown.'
            }
        }
    }
}

function Wait-PreviewDockExit {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process, [Parameter(Mandatory)][string]$Phase)

    $remaining = [Math]::Max(0, [int]($deadline - [DateTimeOffset]::UtcNow).TotalMilliseconds)
    if ($remaining -le 0 -or -not $Process.WaitForExit($remaining)) {
        throw "The $Phase Dock window did not close before the 5-minute watchdog deadline."
    }
    $stopped = Stop-M0OwnedTestProcess -Session $session -Process $Process
    if (-not $stopped.Stopped) { throw "The $Phase Dock process did not stop cleanly." }
}

try {
    $session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
        -Purpose 'GlassDock experimental WPF PreviewOnly: show window, pin two apps, drag reorder, remove one, close, reload saved state, and close' `
        -WatchdogExe $watchdogExe -TimeoutSeconds 300

    # Do not force focus back to the pre-test application: preserve any user focus changes.
    $session.Manifest.ForegroundWindowHandle = 0
    $session.Manifest.ForegroundProcessId = 0
    Write-M0SafetyManifest -Session $session

    $first = Start-PreviewDock
    $dockProcess = $first.Process
    Capture-PreviewWindow -WindowHandle $first.Startup.WindowHandle -OutputPath $firstCapture
    Write-Output ("First view timing and DPI: " + ($first.Startup | ConvertTo-Json -Compress -Depth 4))
    Write-Output 'Preview only is enforced in DockApplicationSession. Finder and System Settings are pinned; Downloads and Trash are in Files. Use + Apps to pin two discovered applications, drag one to reorder, remove one added reference, then close with Exit or Alt+F4. Do not activate any application or file command.'
    Wait-PreviewDockExit -Process $dockProcess -Phase 'first-view'
    $dockProcess = $null

    $firstRunEvents = Get-PreviewEvents
    $successfulPins = @($firstRunEvents | Where-Object {
        $_.Event -eq 'dock-state-mutation' -and $_.Detail.Operation -eq 'PinApplication' -and $_.Detail.Succeeded -and $_.Detail.Changed
    })
    $successfulReorders = @($firstRunEvents | Where-Object {
        $_.Event -eq 'dock-state-mutation' -and $_.Detail.Change -eq 'Reordered' -and $_.Detail.Succeeded -and $_.Detail.Changed
    })
    $successfulRemovals = @($firstRunEvents | Where-Object {
        $_.Event -eq 'dock-state-mutation' -and $_.Detail.Change -eq 'Removed' -and $_.Detail.Succeeded -and $_.Detail.Changed
    })
    if ($successfulPins.Count -lt 2 -or $successfulReorders.Count -lt 1 -or $successfulRemovals.Count -lt 1 -or
        $successfulRemovals[-1].Detail.PinnedCount -lt 2) {
        throw 'The first preview did not record two successful pins, one reorder, one reference removal, and at least two remaining pins; reload was not started.'
    }
    $expectedPinnedHash = $successfulRemovals[-1].Detail.PinnedOrderSha256
    if ([string]::IsNullOrWhiteSpace($expectedPinnedHash)) { throw 'The final saved Dock order fingerprint is missing.' }

    $reload = Start-PreviewDock
    $dockProcess = $reload.Process
    Capture-PreviewWindow -WindowHandle $reload.Startup.WindowHandle -OutputPath $reloadCapture
    Write-Output ("Reload view timing and DPI: " + ($reload.Startup | ConvertTo-Json -Compress -Depth 4))
    $reloadEvents = Get-PreviewEvents
    $loadedConfiguration = @($reloadEvents | Where-Object {
        $_.Event -eq 'configuration-loaded' -and $_.Detail.PinnedOrderSha256 -eq $expectedPinnedHash
    })
    if ($loadedConfiguration.Count -lt 1) { throw 'The reopened Dock configuration did not match the saved pinned-item IDs/order fingerprint.' }
    Write-Output 'Saved pinned IDs and order were read back on the second PreviewOnly startup.'
    Write-Output 'Close the reloaded Dock with its visible Exit button or Alt+F4.'
    Wait-PreviewDockExit -Process $dockProcess -Phase 'reloaded-view'
    $dockProcess = $null
    $completed = $true
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

$finalEvents = Get-PreviewEvents
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
    PreviewOnly = $true
    NativeRoleItems = @('Finder', 'SystemSettings', 'Downloads', 'Trash')
    FirstView = $first.Startup
    ReloadView = $reload.Startup
    RoleIcons = $iconSummary
    FirstScreenshot = Split-Path -Leaf $firstCapture
    ReloadScreenshot = Split-Path -Leaf $reloadCapture
    ConfigurationOrderHash = $expectedPinnedHash
    RestorationPassed = $restoreSummary.RestorePassed
    RestorationEvidencePath = $restoreSummary.RunId
}
[IO.File]::WriteAllText((Join-Path $evidenceRoot 'preview-summary.json'), ($previewSummary | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "Preview test completed. Evidence: $evidenceRoot"
