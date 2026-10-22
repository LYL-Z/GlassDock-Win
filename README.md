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

The scripts place .NET CLI state, NuGet packages, HTTP cache, and temporary files under `.local` in this checkout. The WinUI spike is unpackaged and depends on the Windows App Runtime installed on the machine. It does not enable Windows Developer Mode or require an elevated process. Run `scripts/measure-poc.ps1` for a reproducible idle CPU/memory sample; it briefly displays the selected topmost PoC and closes only the process it started. This script does not measure frame time. The interactive input test in `scripts/verify-poc-input.ps1` covers the desktop with a controlled fullscreen backdrop and injects synthetic mouse input; it requires high-impact confirmation and should only run after the user agrees.

## Boundaries

- This public repository was created at the user's request. The `LICENSE` placeholder remains unchanged; public visibility does not grant a reuse license.
- Machine-local `.local`, `.tools`, `bin`, and `obj` contents are excluded from version control. They contain caches, generated output, and local test evidence; some evidence includes private desktop context.
- No Apple system assets or unlicensed application artwork are included.
- M0 is a technical spike, not a 1:1 visual acceptance or release claim.
- The production desktop entry point remains intentionally empty until the M0 framework gate passes.
