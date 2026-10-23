[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$PlanOnly,
    [switch]$ConfirmInteractiveTest,
    [string]$AuthorizationPhrase,
    [ValidateRange(60, 300)][int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'interactive-test-safety.ps1')

$script:RequiredPhrase = 'RUN M0 MINIMAL DESKTOP TEST'
$script:WatchdogExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.InputProbe\bin\Release\net10.0-windows10.0.19041.0\win-x64\GlassDock.M0.InputProbe.exe'
$script:WinUiExe = Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\GlassDock.PoC.WinUI.exe'
$script:WpfExe = Join-Path $script:RepoRoot 'spikes\GlassDock.PoC.Wpf\bin\Release\net10.0-windows10.0.19041.0\GlassDock.PoC.Wpf.exe'
$script:WgcExe = Join-Path $script:RepoRoot 'spikes\GlassDock.M0.WgcProbe\bin\Release\net10.0-windows10.0.26100.0\WgcProbe.exe'

function Set-M0ProbeEnvironment {
    param(
        [Parameter(Mandatory)][string]$StateRoot,
        [Parameter(Mandatory)][string]$StatusRoot,
        [string]$CompositionMode,
        [string]$VisualCandidate,
        [switch]$NonActivating
    )

    $env:GLASSDOCK_POC_STATE_ROOT = $StateRoot
    $env:GLASSDOCK_POC_ITEM_COUNT = '8'
    $env:GLASSDOCK_POC_ICON_SIZE = '58'
    $env:GLASSDOCK_M0_COMPOSITION_STATUS_ROOT = $StatusRoot
    $env:GLASSDOCK_M0_INPUT_TRACE = '1'
    if ($CompositionMode) { $env:GLASSDOCK_M0_COMPOSITION_PROBE_MODE = $CompositionMode }
    else { Remove-Item Env:GLASSDOCK_M0_COMPOSITION_PROBE_MODE -ErrorAction SilentlyContinue }
    if ($VisualCandidate) { $env:GLASSDOCK_M0_VISUAL_CANDIDATE = $VisualCandidate }
    else { Remove-Item Env:GLASSDOCK_M0_VISUAL_CANDIDATE -ErrorAction SilentlyContinue }
    if ($NonActivating) { $env:GLASSDOCK_M0_NONACTIVATING_START = '1' }
    else { Remove-Item Env:GLASSDOCK_M0_NONACTIVATING_START -ErrorAction SilentlyContinue }
    if ($CompositionMode -eq 'dock' -and $VisualCandidate -eq 'winui-composition-backdrop-blur') { $env:GLASSDOCK_M0_TEST_ROUNDED_REGION = '1' }
    else { Remove-Item Env:GLASSDOCK_M0_TEST_ROUNDED_REGION -ErrorAction SilentlyContinue }
}

function Wait-M0Json {
    param(
        [Parameter(Mandatory)]$Session,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [ValidateRange(1, 60)][int]$Timeout = 15
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($Timeout)
    while ([DateTime]::UtcNow -lt $deadline) {
        Assert-M0InteractiveTestCanContinue $Session
        $Process.Refresh()
        if ($Process.HasExited) { throw "Owned test process exited before writing its ready file: PID=$($Process.Id), exit=$($Process.ExitCode), file=$Path" }
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json } catch { }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for the test ready file: $Path"
}

function Invoke-M0CompositionCapture {
    param(
        [Parameter(Mandatory)]$Session,
        [Parameter(Mandatory)][string]$Role,
        [Parameter(Mandatory)][string]$Mode,
        [string]$VisualCandidate,
        [Parameter(Mandatory)][string]$StateRoot,
        [Parameter(Mandatory)][string]$StatusRoot,
        [Parameter(Mandatory)][string]$ReadyFile,
        [Parameter(Mandatory)][string]$CaptureFile,
        [Parameter(Mandatory)][string]$CompositionStatusFile,
        [switch]$NonActivating
    )

    Set-M0ProbeEnvironment -StateRoot $StateRoot -StatusRoot $StatusRoot -CompositionMode $Mode `
        -VisualCandidate $VisualCandidate -NonActivating:$NonActivating
    $app = Start-M0OwnedTestProcess -Session $Session -FilePath $script:WinUiExe -WorkingDirectory (Split-Path -Parent $script:WinUiExe)
    $windowStatus = Wait-M0Json -Session $Session -Path $ReadyFile -Process $app
    $backgroundStatus = Wait-M0Json -Session $Session `
        -Path (Join-Path $StatusRoot 'controlled-background-window.json') -Process $app
    $brushStatus = if ($VisualCandidate -eq 'winui-composition-backdrop-blur') {
        Wait-M0Json -Session $Session -Path $CompositionStatusFile -Process $app
    } else { $null }
    if ($VisualCandidate -eq 'winui-window-system-acrylic') {
        $dockBounds = $windowStatus.BoundsPx
        $backgroundBounds = $backgroundStatus.BoundsPx
        $dockHwndText = ([string]$windowStatus.Hwnd).ToUpperInvariant()
        $backgroundHwndText = ([string]$backgroundStatus.Hwnd).ToUpperInvariant()
        $dockWidth = [int]$dockBounds.Right - [int]$dockBounds.Left
        $dockHeight = [int]$dockBounds.Bottom - [int]$dockBounds.Top
        $overlapWidth = [Math]::Max(0, [Math]::Min([int]$dockBounds.Right, [int]$backgroundBounds.X + [int]$backgroundBounds.Width) - [Math]::Max([int]$dockBounds.Left, [int]$backgroundBounds.X))
        $overlapHeight = [Math]::Max(0, [Math]::Min([int]$dockBounds.Bottom, [int]$backgroundBounds.Y + [int]$backgroundBounds.Height) - [Math]::Max([int]$dockBounds.Top, [int]$backgroundBounds.Y))
        if ([string]$backgroundStatus.Role -ne 'controlled-background' -or
            $dockHwndText -eq $backgroundHwndText -or
            [int]$backgroundStatus.ProcessId -ne [int]$windowStatus.ProcessId -or
            $dockWidth -le 0 -or $dockHeight -le 0 -or
            ($overlapWidth * $overlapHeight) -ne ($dockWidth * $dockHeight)) {
            throw 'The localized background and Dock were not confirmed as distinct, overlapping HWNDs in the same owned test process; no backdrop screenshot was taken.'
        }
        $windowStatus | Add-Member -NotePropertyName ControlledBackgroundWindow -NotePropertyValue ([ordered]@{
            Hwnd = $backgroundStatus.Hwnd
            ProcessId = $backgroundStatus.ProcessId
            BoundsPx = $backgroundStatus.BoundsPx
            IsDistinctHwnd = $true
            SameOwnedProcess = $true
            FullyCoversDockHwnd = $true
            Note = 'Separate HWND in the same test process; WGC visual capture remains the evidence for external sampling.'
        }) -Force
    }
    Start-Sleep -Milliseconds 600

    if ($Mode -eq 'control') {
        $blurBounds = $windowStatus.BlurControlBoundsPx
        if (-not $blurBounds -or [int]$blurBounds.Width -le 0 -or [int]$blurBounds.Height -le 0) {
            throw 'The independent Composition control did not report a valid rounded-panel capture rectangle.'
        }
        $captureArguments = @('capture-monitor-crop',
            [string]([int]$blurBounds.Left - 160),
            [string]([int]$blurBounds.Top - 160),
            [string]([int]$blurBounds.Width + 320),
            [string]([int]$blurBounds.Height + 320),
            $CaptureFile)
    } else {
        $bounds = $windowStatus.BoundsPx
        if (-not $bounds -or [int]$bounds.Right -le [int]$bounds.Left -or [int]$bounds.Bottom -le [int]$bounds.Top) {
            throw 'The project Dock did not report valid HWND bounds for the localized WGC crop.'
        }
        $captureArguments = @('capture-monitor-crop',
            [string]([int]$bounds.Left - 160),
            [string]([int]$bounds.Top - 160),
            [string]([int]$bounds.Right - [int]$bounds.Left + 320),
            [string]([int]$bounds.Bottom - [int]$bounds.Top + 320),
            $CaptureFile)
    }
    $capture = Start-M0OwnedTestProcess -Session $Session -FilePath $script:WgcExe `
        -WorkingDirectory (Split-Path -Parent $script:WgcExe) -ArgumentList $captureArguments
    if (-not $capture.WaitForExit(15000)) {
        [void](Stop-M0OwnedTestProcess -Session $Session -Process $capture)
        throw "WGC capture helper exceeded its 15-second cap: $Role"
    }
    $capture.Refresh()
    $captureCode = $capture.ExitCode
    if ($captureCode -ne 0) { throw "WGC capture helper failed for $Role; exit=$captureCode; see $([IO.Path]::ChangeExtension($CaptureFile, '.capture-error.txt'))" }

    $imageResult = [ordered]@{
        Role = $Role
        Candidate = if ($Mode -eq 'control') { 'Independent WinUI Composition control window' } else { $VisualCandidate }
        Mode = $Mode
        Edge = if ($Mode -eq 'dock') { 'Bottom' } else { 'Not applicable: centered control surface' }
        IconSizeDip = if ($Mode -eq 'dock') { 58 } else { $null }
        Pattern = 'Opaque black/white 20 DIP vertical stripes in a test-owned localized bottom-center window; Dock crop plus 160 physical-pixel margin persisted'
        WindowStatus = $windowStatus
        CompositionStatus = $brushStatus
        CapturePath = $CaptureFile
        CaptureStatus = 'PASS'
        CaptureIsNotVisualPass = $true
        VisualStatus = 'NOT TESTED - requires visual comparison of sharp stripes against the rounded blur region'
    }
    [IO.File]::WriteAllText((Join-Path $StatusRoot "$Role-result.json"),
        ($imageResult | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    [void](Stop-M0OwnedTestProcess -Session $Session -Process $app)
    return [pscustomobject]$imageResult
}

function Get-M0ForegroundRecord {
    $hwnd = [GlassDockM0SafetyNative]::GetForegroundWindow()
    [uint32]$pidValue = 0
    if ($hwnd -ne [IntPtr]::Zero) { [void][GlassDockM0SafetyNative]::GetWindowThreadProcessId($hwnd, [ref]$pidValue) }
    return [ordered]@{
        Hwnd = $hwnd.ToInt64()
        ProcessId = $pidValue
        Title = if ($hwnd -ne [IntPtr]::Zero) { [GlassDockM0SafetyNative]::GetTitle($hwnd) } else { '' }
    }
}

if ($PlanOnly) {
    [pscustomobject]@{
        Scope = 'One WinUI Window.SystemBackdrop Desktop Acrylic cross-window blur capture at Bottom/58 DIP; one WPF/WinUI non-activating startup comparison; then one no-capture DXGI exclusive-state comparison using the current display mode, 3 seconds per candidate.'
        Background = 'A test-owned opaque black/white stripe window covers a localized bottom-center area (maximum 1000x400 DIP); only the Dock crop plus 160 physical-pixel margin is persisted, on D:.'
        InputInjection = $false
        DisplayModeChangeRequested = $false
        DxgiExclusiveSurfaceShown = $true
        DxgiCapture = $false
        ElevationRequested = $false
        Fullscreen = 'A test-owned DXGI exclusive window is shown for up to 3 seconds per WPF/WinUI candidate at the exact current mode; no game is launched.'
        CaptureCount = 1
        TotalWatchdogLimitSeconds = $TimeoutSeconds
        DxgiWatchdogLimitSeconds = 120
        DxgiStateComparison = 'Included once for WPF and WinUI using verify-poc-exclusive-fullscreen.ps1 -NonActivatingDockStart -SkipCapture -FullscreenSeconds 3; existing mode is queried and restored only if it differs afterward.'
        RequiredPhrase = $script:RequiredPhrase
        EmergencyStop = 'Ctrl+Shift+F12'
        EvidenceRoot = (Join-Path $script:LocalRoot 'evidence\m0-minimal-final')
        DoesNotInclude = @('mouse/keyboard injection', 'DPI/resolution/refresh mode selection', 'real game testing', 'taskbar settings changes', 'WGC capture during DXGI exclusive state')
    } | Format-List
    return
}

foreach ($exe in @($script:WatchdogExe, $script:WinUiExe, $script:WpfExe, $script:WgcExe)) {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Required Release test binary is missing; build first: $exe" }
}
Confirm-M0InteractiveTest -Confirmed:$ConfirmInteractiveTest -AuthorizationPhrase $AuthorizationPhrase `
    -RequiredPhrase $script:RequiredPhrase `
    -Purpose 'briefly show one localized controlled background and WinUI Dock for a single window-level backdrop capture, then show WPF and WinUI non-activating startup candidates while recording foreground ownership'
if (-not $PSCmdlet.ShouldProcess('Current desktop', 'Run one bounded WinUI cross-window backdrop capture and one non-activating startup comparison')) { return }

$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $script:LocalRoot "evidence\m0-minimal-final\$runId"
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$session = Start-M0InteractiveTestSession -RepoRoot $script:RepoRoot -LocalRoot $script:LocalRoot `
    -Purpose 'M0 minimal window-level backdrop and non-activating startup comparison' `
    -WatchdogExe $script:WatchdogExe -TimeoutSeconds $TimeoutSeconds
$results = [System.Collections.Generic.List[object]]::new()
$completed = $false
try {
    Assert-M0InteractiveTestCanContinue $session
    Assert-M0InteractiveTestCanContinue $session
    $dockRoot = Join-Path $runRoot 'project-dock-window-system-acrylic'
    New-Item -ItemType Directory -Force -Path $dockRoot | Out-Null
    $stateRoot = Join-Path $dockRoot 'state'
    New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
    [IO.File]::WriteAllText((Join-Path $stateRoot 'winui-poc-settings.json'), '{"Edge":"Bottom"}', [Text.UTF8Encoding]::new($false))
    $results.Add((Invoke-M0CompositionCapture -Session $session -Role 'project-dock' -Mode 'dock' `
        -VisualCandidate 'winui-window-system-acrylic' -StateRoot $stateRoot -StatusRoot $dockRoot `
        -ReadyFile (Join-Path $dockRoot 'project-dock-window.json') `
        -CaptureFile (Join-Path $dockRoot 'project-dock.png') `
        -CompositionStatusFile (Join-Path $dockRoot 'composition-brush-status.json')))

    $baseline = Get-M0ForegroundRecord
    $originalHwnd = [IntPtr][long]$session.PreviousDesktopState.Foreground.Hwnd
    if ($originalHwnd -eq [IntPtr]::Zero -or -not [GlassDockM0SafetyNative]::IsWindow($originalHwnd)) {
        throw 'The pre-test foreground window is no longer valid; non-activating checks were not started.'
    }
    [void][GlassDockM0SafetyNative]::SetForegroundWindow($originalHwnd)
    Start-Sleep -Milliseconds 200
    $baseline = Get-M0ForegroundRecord
    if ([long]$baseline.Hwnd -ne [long]$session.PreviousDesktopState.Foreground.Hwnd) {
        throw 'Could not safely restore the pre-test foreground window before non-activating checks.'
    }

    foreach ($candidate in @(
        [pscustomobject]@{ Name='WPF'; Exe=$script:WpfExe },
        [pscustomobject]@{ Name='WinUI3'; Exe=$script:WinUiExe }
    )) {
        Assert-M0InteractiveTestCanContinue $session
        $candidateRoot = Join-Path $runRoot "nonactivating-$($candidate.Name)"
        $stateRoot = Join-Path $candidateRoot 'state'
        $statusRoot = Join-Path $candidateRoot 'status'
        New-Item -ItemType Directory -Force -Path $stateRoot,$statusRoot | Out-Null
        if ($candidate.Name -eq 'WinUI3') {
            [IO.File]::WriteAllText((Join-Path $stateRoot 'winui-poc-settings.json'), '{"Edge":"Bottom"}', [Text.UTF8Encoding]::new($false))
        }
        Set-M0ProbeEnvironment -StateRoot $stateRoot -StatusRoot $statusRoot -NonActivating
        $before = Get-M0ForegroundRecord
        $app = Start-M0OwnedTestProcess -Session $session -FilePath $candidate.Exe -WorkingDirectory (Split-Path -Parent $candidate.Exe)
        $deadline = [DateTime]::UtcNow.AddSeconds(12)
        $windowHandle = [IntPtr]::Zero
        do {
            Assert-M0InteractiveTestCanContinue $session
            $app.Refresh()
            if ($app.HasExited) { throw "$($candidate.Name) exited during non-activating startup; exit=$($app.ExitCode)." }
            $windowHandle = [GlassDockM0SafetyNative]::FindFirstVisibleWindowForProcess([uint32]$app.Id)
            if ($windowHandle -ne [IntPtr]::Zero -and [GlassDockM0SafetyNative]::IsWindowVisible($windowHandle)) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($windowHandle -eq [IntPtr]::Zero -or -not [GlassDockM0SafetyNative]::IsWindowVisible($windowHandle)) {
            throw "$($candidate.Name) did not expose a visible HWND within 12 seconds."
        }
        Start-Sleep -Milliseconds 900
        $after = Get-M0ForegroundRecord
        $status = if ([long]$after.Hwnd -eq [long]$before.Hwnd -and [uint32]$after.ProcessId -eq [uint32]$before.ProcessId) {
            'PASS'
        } elseif ([uint32]$after.ProcessId -eq [uint32]$app.Id) {
            'FAIL'
        } else {
            'BLOCKED'
        }
        $reason = if ($status -eq 'PASS') { 'Dock HWND visible; foreground HWND/PID remained unchanged.' }
            elseif ($status -eq 'FAIL') { 'Dock process became the foreground process during startup.' }
            else { 'Foreground changed to an unrelated process during the observation; cannot attribute it to the Dock.' }
        $result = [ordered]@{
            Candidate = $candidate.Name
            StartupMode = if ($candidate.Name -eq 'WPF') { 'Window.ShowActivated=false' } else { 'AppWindow.Show(activateWindow:false)' }
            Status = $status
            Reason = $reason
            Before = $before
            After = $after
            DockProcessId = $app.Id
            DockHwnd = "0x$($windowHandle.ToInt64().ToString('X'))"
            DockWindowVisible = [GlassDockM0SafetyNative]::IsWindowVisible($windowHandle)
            HwndDiscovery = 'EnumWindows; match GetWindowThreadProcessId to the run-owned process; require IsWindowVisible'
            ForegroundObservationDelayMilliseconds = 900
            ForegroundUnchanged = ([long]$after.Hwnd -eq [long]$before.Hwnd -and [uint32]$after.ProcessId -eq [uint32]$before.ProcessId)
            InputInteractionRetested = $false
            InputEvidenceReference = 'Prior M0-07 center-area event-recipient tests; this startup experiment does not inject pointer or keyboard input.'
        }
        [IO.File]::WriteAllText((Join-Path $candidateRoot 'startup-result.json'), ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        $results.Add([pscustomobject]$result)
        [void](Stop-M0OwnedTestProcess -Session $session -Process $app)
        [void][GlassDockM0SafetyNative]::SetForegroundWindow($originalHwnd)
        Start-Sleep -Milliseconds 200
        $restoredHwnd = [GlassDockM0SafetyNative]::GetForegroundWindow()
        Add-M0InteractiveRestorationCheck $session "Foreground reset after $($candidate.Name) startup observation" `
            ([long]$restoredHwnd -eq [long]$originalHwnd) "Observed=0x$($restoredHwnd.ToInt64().ToString('X')); expected=0x$($originalHwnd.ToInt64().ToString('X'))."
        if ([long]$restoredHwnd -ne [long]$originalHwnd) { throw "Could not restore the original foreground window after $($candidate.Name) test." }
    }

    Assert-M0InteractiveTestCanContinue $session
    Restore-M0InteractiveTestEnvironment -Session $session
    $fullscreenScript = Join-Path $PSScriptRoot 'verify-poc-exclusive-fullscreen.ps1'
    $dxgiOutput = @(& $fullscreenScript -ConfirmInteractiveTest -NonActivatingDockStart -SkipCapture `
        -AuthorizationPhrase $script:RequiredPhrase -Candidates @('Wpf', 'WinUI') `
        -FullscreenSeconds 3 -TimeoutSeconds 120 -Confirm:$false)
    Assert-M0InteractiveTestCanContinue $session
    $dxgiEvidenceLine = @($dxgiOutput | ForEach-Object { [string]$_ } | Where-Object { $_ -match '^Evidence: ' -or $_ -match '^Preflight blocked;.*Evidence: ' } | Select-Object -Last 1)
    $dxgiEvidencePath = if ($dxgiEvidenceLine.Count -gt 0) { ($dxgiEvidenceLine[0] -replace '^.*Evidence: ', '').Trim() } else { $null }
    $dxgiSummary = if ($dxgiEvidencePath -and (Test-Path -LiteralPath $dxgiEvidencePath -PathType Leaf)) {
        Get-Content -LiteralPath $dxgiEvidencePath -Raw | ConvertFrom-Json
    } else { $null }
    $results.Add([pscustomobject]@{
        Scenario = 'Non-activating Dock launch during controlled DXGI exclusive state'
        Status = if ($dxgiSummary) { $dxgiSummary.Status } else { 'BLOCKED' }
        EvidencePath = $dxgiEvidencePath
        CandidateResults = if ($dxgiSummary) { @($dxgiSummary.Tests) } else { @() }
        ChildOutput = @($dxgiOutput | ForEach-Object { [string]$_ })
        ScreenshotCaptured = $false
        GameLaunched = $false
    })

    $completed = $true
    [IO.File]::WriteAllText((Join-Path $runRoot 'final-desktop-results.json'),
        ([ordered]@{
            RunId=$runId
            StartedAtUtc=$session.PreviousDesktopState.CapturedAtUtc
            CompletedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
            Scope='one project WinUI Window.SystemBackdrop Desktop Acrylic capture (Bottom/58 DIP); WPF/WinUI non-activating startup observations; and one existing-mode, no-capture DXGI exclusive-state comparison for WPF/WinUI (3 seconds each)'
            Results=@($results)
            FullMonitorFramesPersisted=$false
            OnlyControlledWgcRegionsPersisted=$true
            InputInjected=$false
            DisplaySettingsChanged=$false
            ElevationRequested=$false
        } | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
} catch {
    $runnerError = [ordered]@{
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Type = $_.Exception.GetType().FullName
        Message = $_.Exception.Message
        ScriptStackTrace = $_.ScriptStackTrace
    }
    [IO.File]::WriteAllText((Join-Path $runRoot 'runner-error.json'),
        ($runnerError | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    throw
} finally {
    Restore-M0InteractiveTestEnvironment -Session $session
    $restore = Stop-M0InteractiveTestSession -Session $session -Completed $completed
    if (-not $restore.RestorePassed) { throw "Desktop restoration verification failed. Inspect $($session.EvidenceRoot)\post-state.json" }
    Write-Output "M0 final bounded desktop evidence: $runRoot"
    Write-Output "Restoration checks: $(@($restore.RestorationChecks | Where-Object Passed).Count)/$(@($restore.RestorationChecks).Count) PASS"
}
