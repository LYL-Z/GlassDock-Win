# GlassDock-Win

New Windows 11 Dock prototype. This repository starts from a clean directory and does not use the historical LG-Dsk repository or code.

## M0 status

The M0 acceptance gate is **blocked**. A controlled SendInput test verified that WPF passed transparent-region single-click, double-click, and drag-start routing on Bottom, Left, and Right. WinUI 3 failed transparent-region click-through on all three edges; its content-area and drag scenarios remain untested. One WPF/Light/Bottom capture showed translucency and a shadow, but no background blur. DPI coverage, full-screen/taskbar coexistence, recovery behavior, and valid frame-time evidence remain incomplete. No production framework was selected and M1 has not started. See [M0 report](docs/dock-evidence/M0/M0-report.md), [requirement matrix](docs/dock-evidence/M0/REQ-matrix.md), and [technology comparison](docs/tech-spike-comparison.md).

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

The scripts place .NET CLI state, NuGet packages, HTTP cache, and temporary files under `.local` in this checkout. The WinUI spike is unpackaged and depends on the Windows App Runtime installed on the machine. It does not enable Windows Developer Mode or require an elevated process. `scripts/measure-poc.ps1` collects an idle CPU/memory sample, but it displays the selected topmost PoC and can change foreground focus; it requires `-ConfirmInteractiveTest`, a typed phrase, and PowerShell's high-impact confirmation. It does not measure frame time. The interactive input test in `scripts/verify-poc-input.ps1` covers the desktop with a controlled fullscreen backdrop and injects synthetic mouse input; run either script only after the owner approves its specific desktop impact.

## Boundaries

- This repository is public. GitHub's Terms of Service permit users to view and fork it through GitHub; that does not grant broader rights to reuse, modify, or redistribute the code outside those service functions. The `LICENSE` placeholder is unchanged while the owner decides whether to grant a project license. Its current wording should be reviewed because its broad “Do not redistribute, publish” language does not describe GitHub's view/fork permission; no change is being made to `LICENSE` in this M0 work.
- Machine-local `.local`, `.tools`, `bin`, and `obj` contents are excluded from version control. They contain caches, generated output, and local test evidence; some evidence includes private desktop context.
- No Apple system assets or unlicensed application artwork are included.
- M0 is a technical spike, not a 1:1 visual acceptance or release claim.
- The production desktop entry point remains intentionally empty until the M0 framework gate passes.

## M0 interaction test safety

Scripts that cover the desktop or inject test input are not part of build or unit-test commands. They require `-ConfirmInteractiveTest`, a typed confirmation phrase, and PowerShell's high-impact `ShouldProcess` confirmation. Their watchdog listens for `Ctrl+Shift+F12`, enforces a timeout, and stops only processes whose PID, executable path, and start time were recorded for that run. Do not run these scripts until the owner has approved the specific test window and its desktop impact.
