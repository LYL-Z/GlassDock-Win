# GlassDock.Windows.Tests

Deterministic tests for Windows Shell adapter contracts, Core mapping, icon result bounds, and
launch/activate/open command routing. Native discovery and icon extraction are exercised only by
the opt-in read-only `./scripts/probe-windows-shell.ps1` command; command execution is never invoked
by that probe or by the test suite.
