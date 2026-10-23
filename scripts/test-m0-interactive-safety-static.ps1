$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$safetyPath = Join-Path $PSScriptRoot 'interactive-test-safety.ps1'
$runnerPath = Join-Path $PSScriptRoot 'verify-m0-final-composition-and-activation.ps1'
$exclusivePath = Join-Path $PSScriptRoot 'verify-poc-exclusive-fullscreen.ps1'

# Parse repository PowerShell sources only; do not invoke test runners or UI processes.
$parseErrors = [Collections.Generic.List[string]]::new()
Push-Location $repoRoot
try { $trackedAndWorkingScripts = @(& rg --files -g '*.ps1' -g '!**/.local/**' -g '!**/.tools/**' -g '!**/bin/**' -g '!**/obj/**') }
finally { Pop-Location }
if ($LASTEXITCODE -gt 1) { throw 'ripgrep could not enumerate repository PowerShell sources.' }
foreach ($scriptPath in $trackedAndWorkingScripts) {
        $absolutePath = Join-Path $repoRoot $scriptPath
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($absolutePath, [ref]$tokens, [ref]$errors)
        foreach ($errorItem in $errors) { $parseErrors.Add("${scriptPath}: $($errorItem.Message)") }
}
if ($parseErrors.Count -gt 0) { throw ($parseErrors -join [Environment]::NewLine) }

$runnerText = [IO.File]::ReadAllText($runnerPath)
$finallyIndex = $runnerText.LastIndexOf('} finally {', [StringComparison]::Ordinal)
$restoreIndex = $runnerText.IndexOf('Restore-M0InteractiveTestEnvironment -Session $session', $finallyIndex, [StringComparison]::Ordinal)
$stopIndex = $runnerText.IndexOf('Stop-M0InteractiveTestSession -Session $session', $finallyIndex, [StringComparison]::Ordinal)
if ($finallyIndex -lt 0 -or $restoreIndex -lt $finallyIndex -or $stopIndex -lt $restoreIndex) {
    throw 'Runner finally must restore process-scope environment variables before restoration checks and process cleanup.'
}
if (-not $runnerText.Contains("'RUN M0 MINIMAL DESKTOP TEST'")) { throw 'The new runner authorization phrase is missing.' }
if ($runnerText -match '\bSendInput\s*\(') { throw 'The minimal runner must not inject keyboard or pointer input.' }
if ($runnerText -notmatch 'controlled-background-window\.json' -or
    $runnerText -notmatch 'FullyCoversDockHwnd\s*=\s*\$true') {
    throw 'Runner does not validate that the test-owned background and Dock are distinct, overlapping HWNDs.'
}
if ($runnerText -notmatch 'verify-poc-exclusive-fullscreen\.ps1' -or
    $runnerText -notmatch '-SkipCapture' -or
    $runnerText -notmatch '-FullscreenSeconds 3') {
    throw 'The bounded DXGI state comparison is missing its no-capture/three-second constraints.'
}

$safetyText = [IO.File]::ReadAllText($safetyPath)
$namesMatch = [regex]::Match($safetyText, '(?s)\$script:M0InteractiveTestEnvironmentNames\s*=\s*@\((.*?)\n\)')
if (-not $namesMatch.Success) { throw 'Could not find the process environment snapshot name table.' }
$names = @([regex]::Matches($namesMatch.Groups[1].Value, "'([^']+)'") | ForEach-Object { $_.Groups[1].Value })
if (@($names | Sort-Object -Unique).Count -ne $names.Count) { throw 'The environment snapshot table contains duplicate names.' }
if ($safetyText -notmatch 'Remove-Item\s+-LiteralPath\s+"Env:\$name"' -or
    $safetyText -notmatch '\$null\s+-eq\s+\$expected') {
    throw 'Environment restoration/checks must preserve the difference between unset and empty values.'
}
$functionAst = $null
$tokens = $null
$errors = $null
$safetyAst = [System.Management.Automation.Language.Parser]::ParseFile($safetyPath, [ref]$tokens, [ref]$errors)
$functionAst = $safetyAst.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Restore-M0InteractiveTestEnvironment' }, $true)
if (-not $functionAst) { throw 'Restore-M0InteractiveTestEnvironment function is missing.' }

# Exercise the exact restore function body with process-local sentinels. Preserve the caller's values even if an assertion fails.
$originalValues = [ordered]@{}
foreach ($name in $names) { $originalValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $script:M0InteractiveTestEnvironmentNames = $names
    . ([ScriptBlock]::Create($functionAst.Extent.Text))
    $snapshot = [ordered]@{}
    foreach ($name in $names) { $snapshot[$name] = "M0_STATIC_BEFORE_$name" }
    if ($names.Count -gt 1) { $snapshot[$names[1]] = $null }
    if ($names.Count -gt 2) { $snapshot[$names[2]] = '' }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, "M0_STATIC_MUTATED_$name", 'Process') }
    Restore-M0InteractiveTestEnvironment -Session ([pscustomobject]@{ EnvironmentBefore = $snapshot })
    foreach ($name in $names) {
        $actual = [Environment]::GetEnvironmentVariable($name, 'Process')
        $expected = $snapshot[$name]
        $matchesExpected = if ($null -eq $expected) { $null -eq $actual } else { $actual -ceq [string]$expected }
        if (-not $matchesExpected) { throw "Environment restoration mismatch for $name; expected=[$($snapshot[$name])], actual=[$actual]." }
    }
}
finally {
    foreach ($name in $names) {
        if ($null -eq $originalValues[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, [string]$originalValues[$name], 'Process') }
    }
}

Write-Output "PASS: PowerShell parser accepted all tracked scripts under $repoRoot."
Write-Output "PASS: finally restoration ordering and exact test scope guards are present."
Write-Output "PASS: process environment restore function round-tripped $($names.Count) unique variables, preserving unset and empty-string values separately."
Write-Output 'NOT TESTED: emergency hotkey, watchdog timeout, actual desktop restoration, WGC composition, and DXGI exclusive behavior.'
