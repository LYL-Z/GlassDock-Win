$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$runnerPath = Join-Path $PSScriptRoot 'run-experimental-dock-preview-test.ps1'
$runnerText = [IO.File]::ReadAllText($runnerPath)
$tokens = $null
$parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($runnerPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Preview test runner PowerShell parse failed: $($parseErrors.Message -join '; ')"
}
$visualSmokePath = Join-Path $PSScriptRoot 'run-experimental-dock-visual-smoke.ps1'
$manualInteractionPath = Join-Path $PSScriptRoot 'run-experimental-dock-manual-interaction-test.ps1'
foreach ($entrypoint in @($visualSmokePath, $manualInteractionPath)) {
    $entryTokens = $null
    $entryErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($entrypoint, [ref]$entryTokens, [ref]$entryErrors)
    if ($entryErrors.Count -gt 0) { throw "Preview entrypoint parse failed ($entrypoint): $($entryErrors.Message -join '; ')" }
}
$visualSmokeText = [IO.File]::ReadAllText($visualSmokePath)
$manualInteractionText = [IO.File]::ReadAllText($manualInteractionPath)
$preflightPath = Join-Path $PSScriptRoot 'preview-icon-asset-preflight.ps1'
$preflightText = [IO.File]::ReadAllText($preflightPath)
$preflightTokens = $null
$preflightErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($preflightPath, [ref]$preflightTokens, [ref]$preflightErrors)
if ($preflightErrors.Count -gt 0) { throw "Shared preflight PowerShell parse failed: $($preflightErrors.Message -join '; ')" }
$assetTestsPath = Join-Path $PSScriptRoot 'test-experimental-dock-preview-assets.ps1'
$assetTestsText = [IO.File]::ReadAllText($assetTestsPath)
$assetTestTokens = $null
$assetTestErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($assetTestsPath, [ref]$assetTestTokens, [ref]$assetTestErrors)
if ($assetTestErrors.Count -gt 0) { throw "Preview asset regression test PowerShell parse failed: $($assetTestErrors.Message -join '; ')" }

if ($runnerText -notmatch "RUN GLASSDOCK VISUAL SMOKE TEST" -or
    $runnerText -notmatch "RUN GLASSDOCK MANUAL INTERACTION TEST" -or
    $runnerText -notmatch '\$timeoutSeconds\s*=\s*if \(\$Mode -eq ''VisualSmoke''\) \{ 90 \} else \{ 300 \}') {
    throw 'The visual and manual interaction paths must use distinct exact authorization phrases and independent 90/300-second watchdog budgets.'
}
if ($runnerText -notmatch "WaitForExit\(\[Math\]::Min\(15000" -or
    $runnerText -notmatch 'Start-M0InteractiveTestSession[\s\S]*?-TimeoutSeconds \$timeoutSeconds') {
    throw 'The watchdog budget or bounded WGC capture wait is missing.'
}
if ($runnerText -notmatch "'--preview-only'") { throw 'The runner does not explicitly request PreviewOnly mode.' }
if ($runnerText -notmatch "'--preview-native-role-icons'" -or
    $runnerText -notmatch '\$privateManifest' -or
    $runnerText -notmatch '\$privateSourceRecord' -or
    $runnerText -notmatch '\$privateAssetDirectory' -or
    $runnerText -notmatch 'Copy-Item -LiteralPath \$privateManifest' -or
    $runnerText -notmatch 'Copy-Item -LiteralPath \(Join-Path \$privateAssetDirectory' -or
    $preflightText -notmatch "redistributionStatus -cne 'LocalUseOnly'") {
    throw 'The runner must stage the user-provided local manifest and icon files into the unique evidence profile.'
}
if ($runnerText -notmatch 'Assert-PreviewAssetRootIsLocal' -or
    $runnerText -notmatch 'Invoke-PreviewIconAssetPreflight' -or
    $runnerText -notmatch '\[switch\]\$PreflightOnly' -or
    $preflightText -notmatch "SemanticRole = 'Trash'; VisualState = 'Empty'; SourceRole = 'TrashEmpty'" -or
    $preflightText -notmatch 'Get-FileHash -LiteralPath \$assetPath -Algorithm SHA256' -or
    $preflightText -notmatch 'Get-PreviewPngCrc32' -or
    $preflightText -notmatch 'HasAlphaChannel = \$hasAlphaChannel') {
    throw 'The runner must use the shared full preflight, preserve SHA/PNG integrity checks, record alpha metadata, and map Trash+Empty to TrashEmpty.'
}
$preflightOnlyIndex = $runnerText.IndexOf('if ($PreflightOnly)', [StringComparison]::Ordinal)
$preflightCallIndex = $runnerText.IndexOf('Invoke-PreviewIconAssetPreflight', [StringComparison]::Ordinal)
$confirmationIndex = $runnerText.IndexOf('Confirm-M0InteractiveTest', [StringComparison]::Ordinal)
$normalPreflightIndex = $runnerText.IndexOf('$preflightResult = @(Invoke-PreviewIconAssetPreflight', $confirmationIndex, [StringComparison]::Ordinal)
$sessionStartIndex = $runnerText.IndexOf('Start-M0InteractiveTestSession', [StringComparison]::Ordinal)
$runIdIndex = $runnerText.IndexOf('$runId = ', [StringComparison]::Ordinal)
if ($preflightOnlyIndex -lt 0 -or $preflightCallIndex -lt $preflightOnlyIndex -or
    $confirmationIndex -lt $preflightOnlyIndex -or $normalPreflightIndex -le $confirmationIndex -or
    $sessionStartIndex -lt $normalPreflightIndex -or $runIdIndex -lt $normalPreflightIndex) {
    throw 'The shared preflight must complete before evidence creation, watchdog/session startup, and Dock/WGC process launch; PreflightOnly must return before confirmation or desktop setup.'
}
if ($assetTestsText -notmatch 'Invoke-PreviewIconAssetPreflight' -or
    $assetTestsText -notmatch 'run-experimental-dock-preview-test\.ps1' -or
    $assetTestsText -notmatch 'Trash\+Empty->TrashEmpty' -or
    $assetTestsText -notmatch 'no-alpha/no-transparency' -or
    $assetTestsText -notmatch 'Get-DesktopTestProcessSnapshot') {
    throw 'Asset regression tests must exercise the shared validator, actual runner PreflightOnly path, role mapping, PNG alpha metadata, and no-launch process boundary.'
}
if ($runnerText -match "'--enable-shell-commands'") { throw 'The PreviewOnly runner must never enable Shell commands.' }
if ($runnerText -match '\b(SendInput|SendKeys|keybd_event|mouse_event|ChangeDisplaySettings|SetDisplayConfig|SetThreadDpiAwarenessContext)\s*\(') {
    throw 'The PreviewOnly runner contains input injection or display-setting calls.'
}
if ($runnerText -notmatch "'capture-window'" -or $runnerText -match "'capture-monitor(?:-region|-crop)?'") {
    throw 'The runner must use window-only WGC capture and must not capture the whole display.'
}
if ($runnerText -notmatch 'Start-M0InteractiveTestSession' -or
    $runnerText -notmatch 'Start-M0OwnedTestProcess' -or
    $runnerText -notmatch 'Stop-M0OwnedTestProcess' -or
    $runnerText -notmatch 'finally\s*\{') {
    throw 'The runner must use the independently watched, run-owned process lifecycle with finally cleanup.'
}
$smokeBranchStart = $runnerText.LastIndexOf("if (`$Mode -eq 'VisualSmoke') {", [StringComparison]::Ordinal)
$manualBranchStart = $runnerText.IndexOf('else {', $smokeBranchStart, [StringComparison]::Ordinal)
if ($smokeBranchStart -lt 0 -or $manualBranchStart -le $smokeBranchStart) {
    throw 'The automatic smoke and human interaction paths are not separated into distinct execution branches.'
}
$smokeFlowStart = $runnerText.LastIndexOf('$first = Start-PreviewDock', $smokeBranchStart, [StringComparison]::Ordinal)
$smokeFlow = $runnerText.Substring($smokeFlowStart, $manualBranchStart - $smokeFlowStart)
$smokeBranch = $runnerText.Substring($smokeBranchStart, $manualBranchStart - $smokeBranchStart)
if ($smokeFlow -notmatch 'Wait-PreviewRoleIconsReady' -or
    $smokeFlow -notmatch 'Capture-PreviewWindow' -or
    $smokeBranch -notmatch 'Close-PreviewDockWindow' -or
    $smokeFlow -match 'Wait-PreviewDockExit|AddAppsButton|SendInput|SendKeys') {
    throw 'Visual smoke must capture and close automatically without waiting for manual actions or injecting input.'
}
if ($manualInteractionText -notmatch "'ManualInteraction'" -or
    $manualInteractionText -notmatch 'AuthorizationPhrase' -or
    $runnerText -notmatch 'NOT_TESTED · one or more requested manual actions were not recorded' -or
    $runnerText -notmatch 'PASS · pin, reorder, remove-reference, and reload persistence were observed') {
    throw 'Manual interaction needs its own entrypoint and must report missing actions as NOT_TESTED.'
}
$windowWaitStart = $runnerText.IndexOf('function Wait-PreviewWindowReady', [StringComparison]::Ordinal)
$captureStart = $runnerText.IndexOf('function Capture-PreviewWindow', $windowWaitStart, [StringComparison]::Ordinal)
$exitStart = $runnerText.IndexOf('function Wait-PreviewDockExit', [StringComparison]::Ordinal)
$tryStart = $runnerText.IndexOf('try {', $exitStart, [StringComparison]::Ordinal)
$finallyStart = $runnerText.LastIndexOf('finally {', [StringComparison]::Ordinal)
if ($windowWaitStart -lt 0 -or $captureStart -le $windowWaitStart -or $exitStart -le $captureStart -or $tryStart -le $exitStart -or $finallyStart -le $tryStart) {
    throw 'Could not identify the preview startup, capture, exit, and restoration lifecycle blocks.'
}
$windowWaitBody = $runnerText.Substring($windowWaitStart, $captureStart - $windowWaitStart)
$exitBody = $runnerText.Substring($exitStart, $tryStart - $exitStart)
$finallyBody = $runnerText.Substring($finallyStart)
if ($windowWaitBody -notmatch 'AddSeconds\(\$windowReadyTimeoutSeconds\)' -or
    $windowWaitBody -notmatch '\$Process\.HasExited' -or
    $runnerText -notmatch 'WaitForExit\(\[Math\]::Min\(15000' -or
    $runnerText -notmatch 'captureProcess\.ExitCode\s*-ne\s*0' -or
    $exitBody -notmatch "Result = 'NOT_TESTED'" -or
    $exitBody -notmatch 'manual-interaction-timeout' -or
    $runnerText -notmatch 'PostMessage\(\$WindowHandle, 0x0010' -or
    $finallyBody -notmatch 'Restore-M0InteractiveTestEnvironment -Session \$session' -or
    $finallyBody -notmatch 'Stop-M0InteractiveTestSession -Session \$session' -or
    $finallyBody -notmatch 'if \(-not \$restoreSummary\.RestorePassed\)') {
    throw 'Preview timeout, early process exit, capture failure, normal close, and restoration failure gates are incomplete.'
}
Write-Output 'PASS: Preview startup timeout, early exit, capture failure, orderly close, and fail-closed restoration paths are statically guarded.'

$watchdogPath = Join-Path $repoRoot 'spikes\GlassDock.M0.InputProbe\M0SafetyWatchdog.cs'
$watchdogText = [IO.File]::ReadAllText($watchdogPath)
if ($watchdogText -notmatch 'RegisterHotKey\(IntPtr\.Zero' -or
    $watchdogText -notmatch 'ModControl\s*\|\s*ModShift' -or
    $watchdogText -notmatch 'EmergencyKey\s*=\s*"Ctrl\+Shift\+F12"' -or
    $watchdogText -notmatch 'RegisterHotKey failed' -or
    $watchdogText -notmatch 'watchdog-timeout' -or
    $watchdogText -notmatch 'StopOwnedProcesses\(_options, manifest\)') {
    throw 'The independent watchdog must register Ctrl+Shift+F12, fail closed if registration fails, enforce its deadline, and stop only manifest-owned processes.'
}

$sessionPath = Join-Path $repoRoot 'src\GlassDock.App\DockApplicationSession.cs'
$sessionText = [IO.File]::ReadAllText($sessionPath)
if ($sessionText -notmatch 'DockExecutionMode executionMode\s*=\s*DockExecutionMode\.PreviewOnly') {
    throw 'The application session does not default to PreviewOnly.'
}
$guard = $sessionText.IndexOf('if (ExecutionMode == DockExecutionMode.PreviewOnly)', [StringComparison]::Ordinal)
$providerCall = $sessionText.IndexOf('return _applications.ExecuteAsync', [StringComparison]::Ordinal)
if ($guard -lt 0 -or $providerCall -le $guard) { throw 'PreviewOnly policy does not guard the Shell provider call boundary.' }
if ($sessionText.Substring($guard, $providerCall - $guard) -notmatch 'BlockedByPolicy') {
    throw 'The Shell command guard does not return a policy-blocked command result.'
}

$appPath = Join-Path $repoRoot 'src\GlassDock.Experimental.Wpf\App.xaml.cs'
$appText = [IO.File]::ReadAllText($appPath)
if ($appText -notmatch 'allowShellCommands\s*\?\s*DockExecutionMode\.AllowShellCommands\s*:\s*DockExecutionMode\.PreviewOnly') {
    throw 'The experimental app does not default to PreviewOnly unless the explicit opt-in is present.'
}
if ($appText -notmatch 'case "--preview-native-role-icons"' -or
    $appText -notmatch 'includeNativeRolePreview\s*&&\s*\(!previewOnly\s*\|\|\s*allowShellCommands' -or
    $appText -notmatch 'IsIsolatedPreviewProfile\(resolvedProfile, resolvedDiagnostics') {
    throw 'Native-role previews must be explicitly enabled, remain PreviewOnly, and use a run-owned isolated profile.'
}

$seedPath = Join-Path $repoRoot 'src\GlassDock.App\DockNativeRolePreviewSeed.cs'
$seedText = [IO.File]::ReadAllText($seedPath)
if ($seedText -notmatch 'windows\.file-manager' -or
    $seedText -notmatch 'windows\.settings' -or
    $seedText -notmatch 'windows\.recycle-bin' -or
    $seedText -notmatch 'WindowsKnownFolderId' -or
    $seedText -notmatch '"Finder"' -or
    $seedText -notmatch '"Downloads"' -or
    $seedText -notmatch '"Trash"') {
    throw 'The isolated seed does not map all four visible roles to separate Windows identities.'
}

$windowXaml = [IO.File]::ReadAllText((Join-Path $repoRoot 'src\GlassDock.Experimental.Wpf\DockWindow.xaml'))
$windowCode = [IO.File]::ReadAllText((Join-Path $repoRoot 'src\GlassDock.Experimental.Wpf\DockWindow.xaml.cs'))
if ($windowXaml -match 'GLASSDOCK\s*/\s*EXPERIMENTAL|PREVIEW ONLY|StatusLabel|PositionButton|ExitButton|Add Apps' -or
    $windowCode -notmatch 'Header = "Exit Preview"' -or
    $windowCode -notmatch 'KeyGesture\(Key\.F4, ModifierKeys\.Alt\)' -or
    $windowCode -notmatch 'Key\.Q' -or
    $windowXaml -notmatch 'CornerRadius="38"|CornerRadius="14"') {
    throw 'The Dock visual surface still exposes the debug toolbar, lacks a compact continuous backplate, or lost its right-click Exit/keyboard exit route.'
}
if ($windowCode -notmatch 'KeyGesture\(Key\.F4, ModifierKeys\.Alt\)' -or
    $windowCode -notmatch '_catalogLoadCancellation\?\.Cancel\(\)' -or
    $windowCode -notmatch '_iconLoadCancellation\?\.Cancel\(\)') {
    throw 'Alt+F4 or async window-resource cancellation is missing.'
}
if ($windowCode -notmatch 'DockVisualGeometry\.Measure' -or
    $windowCode -notmatch 'VisualLayoutVerified' -or
    $windowCode -notmatch 'Backplate\.ActualHeight' -or
    $windowCode -notmatch 'DockSection\.RecentApplications' -or
    $windowCode -notmatch 'DockSection\.FilesAndFolders') {
    throw 'The WPF host must apply framework-neutral measured geometry and retain the three section semantics.'
}
$geometryPath = Join-Path $repoRoot 'src\GlassDock.Core\DockVisualGeometry.cs'
$geometryTestsPath = Join-Path $repoRoot 'tests\GlassDock.Core.Tests\DockVisualGeometryTests.cs'
$geometryText = [IO.File]::ReadAllText($geometryPath)
$geometryTestsText = [IO.File]::ReadAllText($geometryTestsPath)
if ($geometryText -notmatch 'public static class DockVisualGeometry' -or
    $geometryText -notmatch 'public static DockVisualLayout Measure' -or
    $geometryTestsText -notmatch '\[DataRow\(44d\)\]' -or
    $geometryTestsText -notmatch '\[DataRow\(48d\)\]' -or
    $geometryTestsText -notmatch '\[DataRow\(58d\)\]' -or
    $geometryTestsText -notmatch '\[DataRow\(72d\)\]' -or
    $geometryTestsText -notmatch 'HorizontalDockKeepsEveryIconInsideItsTileAndRoundedBackplate' -or
    $geometryTestsText -notmatch 'ConstrainedAxisScrollsWithoutClippingIconCrossSection') {
    throw 'Framework-neutral Dock geometry and clipping regression coverage for the requested icon sizes is missing.'
}
if ($windowCode -notmatch 'WindowInteropHelper\(this\)\.Handle' -or
    $windowCode -notmatch 'SourceInitialized\s*\+=\s*Window_SourceInitialized' -or
    $windowCode -notmatch 'window-hwnd-created' -or
    $windowCode -notmatch 'window-visible' -or
    $windowCode -notmatch 'window-layout-prepared' -or
    $windowCode -notmatch 'window-content-rendered' -or
    $windowCode -notmatch 'StartStateIconLoading\(\)' -or
    $windowCode -match 'await\s+LoadStateIconsAsync\(') {
    throw 'WPF must report HWND, visibility, layout, and first render independently, then load icons without gating first visibility.'
}
$enumeratorPath = Join-Path $repoRoot 'src\GlassDock.Windows\Windowing\DockTopLevelWindowEnumerator.cs'
$enumeratorText = [IO.File]::ReadAllText($enumeratorPath)
if ($runnerText -match '\$Process\.MainWindowHandle' -or
    $runnerText -notmatch 'DockTopLevelWindowEnumerator\]::Enumerate' -or
    $enumeratorText -notmatch 'EnumWindows' -or
    $enumeratorText -notmatch 'GetWindowThreadProcessId' -or
    $enumeratorText -notmatch 'IsWindowVisible' -or
    $enumeratorText -notmatch 'GetWindowRect' -or
    $runnerText -notmatch 'window-hwnd-created' -or
    $runnerText -notmatch 'window-layout-prepared' -or
    $runnerText -notmatch 'window-content-rendered' -or
    $runnerText -notmatch 'DockWindowReadinessPolicy\]::Evaluate' -or
    $runnerText -notmatch 'VisibleCandidateCount\s*=\s*\$readiness\.VisibleCandidateCount' -or
    $runnerText -notmatch 'refusing an ambiguous WGC target') {
    throw 'The preview runner must validate the SourceInitialized HWND by Win32 PID, visibility, bounds, uniqueness, layout, and first render.'
}
$appText = [IO.File]::ReadAllText($appPath)
$diagnosticsPath = Join-Path $repoRoot 'src\GlassDock.Experimental.Wpf\ExperimentalDockDiagnostics.cs'
$diagnosticsText = [IO.File]::ReadAllText($diagnosticsPath)
if ($runnerText -notmatch 'Get-PreviewEvents\s+-ProcessId\s+\$Process\.Id' -or
    $runnerText -notmatch 'Assert-PreviewContentReady\s+-Process\s+\$process' -or
    $diagnosticsText -notmatch 'ProcessId\s*=\s*Environment\.ProcessId' -or
    $appText -notmatch 'ProcessId\s*=\s*Environment\.ProcessId' -or
    $windowCode -notmatch 'ProcessId\s*=\s*Environment\.ProcessId') {
    throw 'Readiness, manifest, seed, and catalog events must be scoped to the process launched for this view so prior-run log records cannot be reused.'
}
if ($windowCode -notmatch 'DragStarted:\s*true' -or $windowCode -notmatch 'DragDrop\.DoDragDrop') {
    throw 'A started drag is not explicitly suppressed from invoking the tile command.'
}
if ($windowCode -notmatch 'dock-icon-resolved' -or
    $windowCode -notmatch 'VisualState = finalVisualState\?\.ToString\(\)' -or
    $windowCode -notmatch 'Decoded = image is not null' -or
    $windowCode -notmatch 'DockIconLoadBatch\.RunAsync' -or
    $windowCode -notmatch 'Icon unavailable; showing a safe fallback' -or
    $runnerText -notmatch 'Wait-PreviewRoleIconsReady' -or
    $windowCode -notmatch 'state-icon-load-started') {
    throw 'Icons must load progressively with safe placeholders/fallbacks and have separately observable resolution timing.'
}

$readinessPolicyPath = Join-Path $repoRoot 'src\GlassDock.Windows\Windowing\DockWindowReadiness.cs'
$readinessPolicyText = [IO.File]::ReadAllText($readinessPolicyPath)
$readinessTestsPath = Join-Path $repoRoot 'tests\GlassDock.Windows.Tests\DockWindowReadinessPolicyTests.cs'
$readinessTestsText = [IO.File]::ReadAllText($readinessTestsPath)
if ($readinessPolicyText -notmatch 'reportedHandle\s*==\s*nint\.Zero' -or
    $readinessPolicyText -notmatch 'reported\.ProcessId' -or
    $readinessPolicyText -notmatch 'reported\.IsVisible' -or
    $readinessPolicyText -notmatch 'reported\.Bounds\.IsUsable' -or
    $readinessPolicyText -notmatch 'MultipleVisibleCandidates' -or
    $readinessPolicyText -notmatch 'layoutPrepared' -or
    $readinessPolicyText -notmatch 'contentRendered' -or
    $readinessTestsText -notmatch 'ZeroHwndIsRejected' -or
    $readinessTestsText -notmatch 'ReportedHwndOwnedByAnotherProcessIsRejected' -or
    $readinessTestsText -notmatch 'InvisibleReportedWindowIsRejected' -or
    $readinessTestsText -notmatch 'InvalidWindowBoundsAreRejected' -or
    $readinessTestsText -notmatch 'ValidEnumeratedHwndIsAcceptedWhenProcessMainWindowHandleWouldBeZero' -or
    $readinessTestsText -notmatch 'MultipleVisibleWindowsFromDockProcessAreRejected') {
    throw 'The pure HWND readiness policy must reject invalid candidates and test the MainWindowHandle-zero WPF case without desktop APIs.'
}
$batchTestsPath = Join-Path $repoRoot 'tests\GlassDock.App.Tests\DockIconLoadBatchTests.cs'
$batchTestsText = [IO.File]::ReadAllText($batchTestsPath)
$recycleProviderPath = Join-Path $repoRoot 'src\GlassDock.Windows\Shell\WindowsRecycleBinIconStateProvider.cs'
$recycleProviderText = [IO.File]::ReadAllText($recycleProviderPath)
if ($batchTestsText -notmatch 'SlowIconDoesNotBlockFastIcon' -or
    $batchTestsText -notmatch 'ConcurrencyLimit' -or
    $recycleProviderText -notmatch 'DefaultWaitBudget\s*=\s*TimeSpan\.FromSeconds\(1\)' -or
    $recycleProviderText -notmatch 'WaitAsync\(_waitBudget, cancellationToken\)' -or
    $recycleProviderText -notmatch 'recycle-bin-query-timeout' -or
    $recycleProviderText -notmatch 'NativeCallStillRunning') {
    throw 'The icon batch must be concurrency-bounded, and recycle-bin status waits must have a real timeout without claiming the native call was cancelled.'
}

$safetyPath = Join-Path $PSScriptRoot 'interactive-test-safety.ps1'
$safetyText = [IO.File]::ReadAllText($safetyPath)
if ($runnerText -match 'PreviousDesktopState\.Foreground\.(Hwnd|ProcessId)\s*=\s*0' -or
    $safetyText -notmatch '\$Session\.Manifest\.ForegroundWindowHandle' -or
    $safetyText -notmatch '\$Session\.Manifest\.ForegroundProcessId' -or
    $safetyText -notmatch 'SetThreadDpiAwarenessContext\(\$previousDpiContext\)' -or
    $safetyText -notmatch 'finally\s*\{\s*if \(\$dpiContextSwitchSucceeded\)' -or
    $safetyText -notmatch 'Foreground policy left no run-owned process focused') {
    throw 'The run must restore its temporary DPI thread context, retain the pre-state snapshot, and keep focus policy separate.'
}

$testsPath = Join-Path $repoRoot 'tests\GlassDock.App.Tests\DockApplicationSessionTests.cs'
$testsText = [IO.File]::ReadAllText($testsPath)
if ($testsText -notmatch 'PreviewOnlyBlocksLaunchActivateOpenAndApplicationInvocationBeforeProviderBoundary' -or
    $testsText -notmatch 'RepeatedCatalogRefreshPreservesPinnedIdsOrderAndDoesNotPersistDiscoveredApps') {
    throw 'PreviewOnly command-boundary or repeated-refresh regression coverage is missing.'
}
$seedTestsPath = Join-Path $repoRoot 'tests\GlassDock.App.Tests\DockNativeRolePreviewSeedTests.cs'
if (-not (Test-Path -LiteralPath $seedTestsPath -PathType Leaf)) {
    throw 'Native-role preview state identity, immutability, and configuration round-trip tests are missing.'
}

Write-Output 'PASS: PreviewOnly desktop test script parses and requires a fresh exact authorization phrase.'
Write-Output 'PASS: Visual smoke has a 90-second watchdog budget; manual interaction has a separate 300-second budget and distinct authorization phrase.'
Write-Output 'PASS: Runner requests PreviewOnly, uses window-only WGC, and has no input-injection or display-setting code.'
Write-Output 'PASS: Actual runner and regression tests share one full role/state/source, hash, PNG-structure, and transparency preflight; dry run returns before desktop setup.'
Write-Output 'PASS: Private user icons and manifest are staged only into a unique D: evidence profile; native roles require explicit PreviewOnly opt-in.'
Write-Output 'PASS: Watchdog source registers Ctrl+Shift+F12, fails closed on registration failure, enforces timeout, and terminates manifest-owned processes only.'
Write-Output 'PASS: Session blocks Shell commands before the provider; WPF keeps right-click Exit, Alt+F4/Ctrl+Q, cleanup, and drag-click suppression outside the visual strip.'
Write-Output 'PASS: The Dock body is an icon-only capsule with section separators; framework-neutral geometry covers 44/48/58/72 DIP and smoke assertions reject clipped icon bounds.'
Write-Output 'PASS: PreviewOnly command and repeated catalog refresh regression tests are present.'
Write-Output 'NOT TESTED: actual Dock display, WGC capture, hotkey registration, timeout termination, and desktop restoration.'
