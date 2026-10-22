# GlassDock-Win

New Windows 11 Dock prototype. This repository starts from a clean directory and does not use the historical LG-Dsk repository or code.

## M0 status

The M0 engineering report and local evidence matrix are complete, but the acceptance gate is **blocked**. Both WinUI 3 and WPF spikes build and launch locally. WPF passed the observed OS transparent-corner hit query on all three edges; WinUI failed it on all three. Frame timing, 100/150/200% DPI, taskbar/fullscreen, light/dark visual transmission, tray-exit interaction, and project licensing remain unresolved. No production framework was selected and M1 has not started. See [M0 report](docs/dock-evidence/M0/M0-report.md), [requirement matrix](docs/dock-evidence/M0/REQ-matrix.md), and [technology comparison](docs/tech-spike-comparison.md). No remote repository is configured. The placeholder `LICENSE` grants no permission to redistribute this source.

## Build

The project pins .NET SDK `10.0.402` in `global.json`. On Windows x64, install it under `.tools\dotnet` without administrator privileges:

```powershell
New-Item -ItemType Directory -Force .tools | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile .tools/dotnet-install.ps1
.\.tools\dotnet-install.ps1 -Version 10.0.402 -Architecture x64 -InstallDir .tools/dotnet -NoPath
```

Then build and test from this directory (also validated from a clean local clone on D:):

```powershell
.\scripts\build.ps1
.\scripts\test.ps1 -NoRestore
```

The scripts place .NET CLI state, NuGet packages, HTTP cache, and temporary files under `.local` in this checkout. The WinUI spike is unpackaged and depends on the Windows App Runtime installed on the machine. It does not enable Windows Developer Mode or require an elevated process. Run `scripts/measure-poc.ps1` for a reproducible idle CPU/memory sample; it briefly displays the selected topmost PoC and closes only the process it started. This script does not measure frame time.

## Boundaries

- Local project only; no remote is configured or pushed by this task.
- No Apple system assets or unlicensed application artwork are included.
- M0 is a technical spike, not a 1:1 visual acceptance or release claim.
- The production desktop entry point remains intentionally empty until the M0 framework gate passes.
