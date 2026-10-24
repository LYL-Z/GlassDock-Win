[CmdletBinding()]
param(
    [switch]$ConfirmDesktopTest,
    [string]$AuthorizationPhrase
)

$ErrorActionPreference = 'Stop'
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

$runId = [Guid]::NewGuid().ToString('N')
$evidenceRoot = Join-Path $script:LocalRoot "evidence\experimental-preview\$runId"
$profileRoot = Join-Path $evidenceRoot 'profile'
$diagnosticsPath = Join-Path $evidenceRoot 'dock-diagnostics.jsonl'
$firstCapture = Join-Path $evidenceRoot 'dock-first-view.png'
$reloadCapture = Join-Path $evidenceRoot 'dock-reloaded-view.png'
New-Item -ItemType Directory -Path $profileRoot -Force | Out-Null
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(300)
$session = $null
$dockProcess = $null
$completed = $false

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
    $args = @('--preview-only', '--profile-dir', $profileRoot, '--diagnostics-path', $diagnosticsPath)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-M0OwnedTestProcess -Session $session -FilePath $appExe -ArgumentList $args `
        -WorkingDirectory (Split-Path -Parent $appExe)
    $process.Refresh()
    $startup = Wait-PreviewWindowReady -Process $process -Phase 'Dock preview'
    $startup | Add-Member -NotePropertyName LauncherStartToReadyMilliseconds -NotePropertyValue ([Math]::Round($timer.Elapsed.TotalMilliseconds, 3))
    return [pscustomobject]@{ Process = $process; Startup = $startup }
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
    $session.PreviousDesktopState.Foreground.Hwnd = 0
    $session.PreviousDesktopState.Foreground.ProcessId = 0
    Write-M0SafetyManifest -Session $session

    $first = Start-PreviewDock
    $dockProcess = $first.Process
    Capture-PreviewWindow -WindowHandle $first.Startup.WindowHandle -OutputPath $firstCapture
    Write-Output 'Preview only is enforced in DockApplicationSession. Pin two entries, drag one to reorder, remove one reference, then close with Exit or Alt+F4. Do not activate any application command.'
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
        $summary = Stop-M0InteractiveTestSession -Session $session -Completed $completed
        if (-not $summary.RestorePassed) { throw 'Desktop recovery checks failed. Do not rerun; inspect the saved post-state.json first.' }
    }
}

Write-Output "Preview test completed. Evidence: $evidenceRoot"
