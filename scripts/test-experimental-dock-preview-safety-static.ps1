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

if ($runnerText -notmatch '\$requiredPhrase\s*=\s*''RUN GLASSDOCK PREVIEW TEST''') {
    throw 'The exact independent desktop-test authorization phrase is missing.'
}
if ($runnerText -notmatch '-TimeoutSeconds\s+300' -or $runnerText -notmatch "WaitForExit\(\[Math\]::Min\(15000") {
    throw 'The 5-minute watchdog or bounded capture wait is missing.'
}
if ($runnerText -notmatch "'--preview-only'") { throw 'The runner does not explicitly request PreviewOnly mode.' }
if ($runnerText -notmatch "'--preview-native-role-icons'" -or
    $runnerText -notmatch '\$privateManifest' -or
    $runnerText -notmatch '\$privateSourceRecord' -or
    $runnerText -notmatch '\$privateAssetDirectory' -or
    $runnerText -notmatch 'Copy-Item -LiteralPath \$privateManifest' -or
    $runnerText -notmatch 'Copy-Item -LiteralPath \(Join-Path \$privateAssetDirectory' -or
    $runnerText -notmatch 'Get-FileHash -LiteralPath \$iconPath' -or
    $runnerText -notmatch 'redistributionStatus -cne ''LocalUseOnly''') {
    throw 'The runner must stage the user-provided local manifest and icon files into the unique evidence profile.'
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
if ($windowXaml -notmatch 'x:Name="ExitButton"' -or $windowXaml -notmatch 'Click="ExitButton_Click"') {
    throw 'The Dock window does not expose a visible Exit button.'
}
if ($windowCode -notmatch 'KeyGesture\(Key\.F4, ModifierKeys\.Alt\)' -or
    $windowCode -notmatch '_catalogLoadCancellation\?\.Cancel\(\)' -or
    $windowCode -notmatch '_iconLoadCancellation\?\.Cancel\(\)') {
    throw 'Alt+F4 or async window-resource cancellation is missing.'
}
if ($windowCode -notmatch 'DragStarted:\s*true' -or $windowCode -notmatch 'DragDrop\.DoDragDrop') {
    throw 'A started drag is not explicitly suppressed from invoking the tile command.'
}
if ($windowCode -notmatch 'dock-icon-resolved' -or
    $windowCode -notmatch 'VisualState = finalVisualState\?\.ToString\(\)' -or
    $windowCode -notmatch 'Decoded = image is not null') {
    throw 'Per-role icon source, visual-state, and decode outcomes must be observable in D: diagnostics.'
}

$safetyPath = Join-Path $PSScriptRoot 'interactive-test-safety.ps1'
$safetyText = [IO.File]::ReadAllText($safetyPath)
if ($runnerText -match 'PreviousDesktopState\.Foreground\.(Hwnd|ProcessId)\s*=\s*0' -or
    $safetyText -notmatch '\$Session\.Manifest\.ForegroundWindowHandle' -or
    $safetyText -notmatch '\$Session\.Manifest\.ForegroundProcessId') {
    throw 'The run must retain the original desktop snapshot and keep foreground-restoration policy separate.'
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
Write-Output 'PASS: Runner is bounded to 300 seconds, uses an independent watchdog, and only tracks run-owned processes.'
Write-Output 'PASS: Runner requests PreviewOnly, uses window-only WGC, and has no input-injection or display-setting code.'
Write-Output 'PASS: Private user icons and manifest are staged only into a unique D: evidence profile; native roles require explicit PreviewOnly opt-in.'
Write-Output 'PASS: Watchdog source registers Ctrl+Shift+F12, fails closed on registration failure, enforces timeout, and terminates manifest-owned processes only.'
Write-Output 'PASS: Session blocks Shell commands before the provider; WPF has visible Exit, Alt+F4, cleanup, and drag-click suppression.'
Write-Output 'PASS: PreviewOnly command and repeated catalog refresh regression tests are present.'
Write-Output 'NOT TESTED: actual Dock display, WGC capture, hotkey registration, timeout termination, and desktop restoration.'
