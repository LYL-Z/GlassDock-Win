# GlassDock-Win

M0 technical proof of concept for evaluating Windows APIs and UI frameworks for a future Dock-style desktop application. This repository starts from a clean directory and does not use the historical LG-Dsk repository or code. It is not a complete Dock and does not claim a 1:1 macOS Dock recreation.

## M0 status

M0 concluded with **NO-GO; the Windows window/rendering route is not frozen**. By a later explicit phase-dependency change, an independent M1 product-specification research line is now active in draft form; this is not M1 acceptance and does not authorize production renderer work. The independent Composition control showed same-window blur, but the current Dock window did not; the non-activating observation was blocked and the authorized run stopped before WinUI/fullscreen observations. No original requirement is erased or converted to PASS. See [the phase dependency change](docs/dock-evidence/M0/M0-M1-parallel-work-change-2026-10-23.md), [the 46-item M1 specification draft](docs/dock-evidence/M1/spec-matrix.csv), and [M0 final technical decision](docs/dock-evidence/M0/M0-final-technical-decision.md).

Current evidence: WPF default click routing passed 18/18 tested cases; WinUI default transparent routing failed 9/9; the opt-in public `SetWindowRgn` candidate passed 18/18 input cases. Prior WGC comparison of WPF DWM Acrylic and WinUI Desktop Acrylic variants found no target background blur. The final bounded Composition run showed blur in its independent WinUI control but not in the project Dock window. The non-activating observation stopped because the harness did not locate the WPF HWND; WinUI and DXGI companion observations were not run. Default startup observations previously found WPF foreground activation and WinUI losing DXGI exclusive state. Dock-specific performance distributions are not measured. See [M0 final decision report](docs/dock-evidence/M0/M0-final-technical-decision.md), [scope change](docs/dock-evidence/M0/M0-scope-change-2026-10-23.md), [33-ID migration matrix](docs/dock-evidence/M0/M0-to-M1-requirement-migration.md), [baseline requirement matrix](docs/dock-evidence/M0/REQ-matrix.md), and [technology comparison](docs/tech-spike-comparison.md).

The independently authorized M1 research line is active as a draft only: [46-item specification matrix](docs/dock-evidence/M1/spec-matrix.csv), [reference baseline and missing-sample manifest](docs/dock-evidence/M1/reference-baseline.md), [default item catalog](docs/dock-evidence/M1/default-item-catalog.md), [behavior proposal](docs/dock-evidence/M1/behavior-specification-draft.md), and [asset rights ledger](docs/dock-evidence/M1/rights-and-reference-ledger.md). Production renderer/HWND work remains gated by M0, and no Mac hardware sample has been recorded.

## Experimental Dock preview

The repository includes a runnable **Experimental / PreviewOnly WPF Dock** with local semantic icon mappings, Pinned/Recent/Files sections, basic pin/remove/reorder and persisted preview state. The normal [experimental runner](scripts/run-experimental-dock.ps1) starts in PreviewOnly; Launch, Activate, and Open are blocked at the session-to-Shell boundary. This replaceable host does not select the production window or rendering architecture. User-provided Apple icons stay in the ignored D: `.local` directory and are absent from Git and build resources. Cross-window background blur, Liquid Glass, full manual interaction acceptance, and 1:1 macOS visual acceptance remain incomplete. See the [experimental slice scope](docs/experimental-dock-slice.md) and [local icon mapping guide](docs/native-icon-assets.md).

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

Scripts that cover the desktop or inject test input are not part of build or unit-test commands. They require `-ConfirmInteractiveTest`, a test-specific phrase, and PowerShell `ShouldProcess`. The bounded M0 closeout scripts use a 300-second watchdog, listen for `Ctrl+Shift+F12`, and stop only processes whose PID, executable path, and start time were recorded for that run. Do not run them until the owner has approved the specific test window and its desktop impact.
