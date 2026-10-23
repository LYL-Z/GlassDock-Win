# GlassDock-Win

M0 technical proof of concept for evaluating Windows APIs and UI frameworks for a future Dock-style desktop application. This repository starts from a clean directory and does not use the historical LG-Dsk repository or code. It is not a complete Dock and does not claim a 1:1 macOS Dock recreation.

## M0 status

The M0 acceptance gate is **not passed**. WPF's default click-routing test passed 18/18 controlled scenarios across Bottom, Left, and Right. WinUI 3's default transparent-region routing failed, while an opt-in public `SetWindowRgn` candidate passed 18/18 input scenarios; the region candidate is not the default or a frozen framework decision. Baseline WGC captures show translucency but no target background blur. New opt-in WinUI Desktop Acrylic and WPF DWM system-backdrop candidates compile but have not been displayed or captured. Exclusive-fullscreen evidence records the Dock as foreground for both default startups; WinUI also loses DXGI exclusive state. Dock invisibility by itself is allowed, but input routing and automatic recovery remain untested. WinUI tray command routing now has 3/3 noninteractive unit tests; actual menu selection and accessibility paths remain untested. App-side timing telemetry is implemented but no runtime trace has been collected. No production framework was selected and M1 has not started. See [M0 report](docs/dock-evidence/M0/M0-report.md), [requirement matrix](docs/dock-evidence/M0/REQ-matrix.md), and [technology comparison](docs/tech-spike-comparison.md).

## Build

The project pins .NET SDK `10.0.402` in `global.json`. On Windows x64, install it under `.tools\dotnet` without administrator privileges:

```powershell
New-Item -ItemType Directory -Force .tools | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile .tools/dotnet-install.ps1
.\.tools\dotnet-install.ps1 -Version 10.0.402 -Architecture x64 -InstallDir .tools/dotnet -NoPath
```

Then build and test from this directory:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1 -NoRestore
```

The scripts place .NET CLI state, NuGet packages, HTTP cache, and temporary files under `.local` in this checkout. The WinUI spike is unpackaged and depends on the Windows App Runtime installed on the machine. It does not enable Windows Developer Mode or require an elevated process. `scripts/measure-poc.ps1 -EnableM0Telemetry` can record opt-in app-side logical-update intervals, framework render callbacks, animation durations, and per-process CPU/memory to D:. Framework callbacks are proxy measurements; the trace does not measure GPU submission, per-process DWM presents, or panel scanout. The script displays the selected topmost PoC and can change foreground focus; it requires `-ConfirmInteractiveTest`, a typed phrase, and PowerShell's high-impact confirmation. Input, visual-capture, window-coexistence, refresh-mode, tray, and exclusive-fullscreen scripts can cover the desktop or change focus; input tests also inject synthetic mouse events. Run these scripts only after the owner approves the specific desktop test and its impact.

## Boundaries

- This repository is public. GitHub's Terms of Service permit users to view and fork it through GitHub; that does not grant broader rights to reuse, modify, or redistribute the code outside those service functions. The `LICENSE` placeholder is unchanged while the owner decides whether to grant a project license. Its current wording should be reviewed because its broad “Do not redistribute, publish” language does not describe GitHub's view/fork permission; no change is being made to `LICENSE` in this M0 work.
- Machine-local `.local`, `.tools`, `bin`, and `obj` contents are excluded from version control. They contain caches, generated output, and local test evidence; some evidence includes private desktop context.
- No Apple system assets or unlicensed application artwork are included.
- M0 is a technical spike, not a 1:1 visual acceptance or release claim.
- The production desktop entry point remains intentionally empty until the M0 framework gate passes.

## M0 interaction test safety

Scripts that cover the desktop or inject test input are not part of build or unit-test commands. They require `-ConfirmInteractiveTest`, a typed confirmation phrase, and PowerShell's high-impact `ShouldProcess` confirmation. Their watchdog listens for `Ctrl+Shift+F12`, enforces a timeout, and stops only processes whose PID, executable path, and start time were recorded for that run. Do not run these scripts until the owner has approved the specific test window and its desktop impact.
