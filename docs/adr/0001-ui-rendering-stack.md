# ADR 0001: UI and rendering stack

- Status: M0 gate blocked; no production decision
- Date: 2026-10-22

## Context

The first prototype must support an undecorated, translucent, edge-positioned Dock with eight placeholder icons. Compare WinUI 3 with Windows App SDK Composition and WPF with Windows Composition interop using the same geometry and sample content. Assess Win32 + DirectComposition/Direct2D/Skia as the third route and state how the analysis could be disproved.

## Decision

Not made. Both candidate spikes build and launch on this machine. At the current reported 240 DPI, WPF's transparent-corner `WindowFromPoint` query passed on all three edges; WinUI's failed on all three. Idle CPU/memory values are short and mixed, while frame time could not be captured because PresentMon's ETW session was denied. The main desktop project remains an empty class library.

## Evidence required

- Windows version/build, SDK/runtime, display and DPI settings.
- Build and launch commands and exit codes for both spikes.
- Observed transparency and alpha-hole hit testing, resize, edge placement, light/dark backdrop, and failure cases.
- Process CPU, working-set/private bytes, and frame-time method, with the sample size and limitations stated.
- Third-route estimate grounded in official API documentation and a falsifiable next experiment.
- 100/150/200% DPI, taskbar/fullscreen, light/dark visual transmission, actual underlying-app clicks, tray Exit interaction, and project license confirmation.

## Consequences

Until these evidence gaps are closed, no candidate is treated as the production framework. Do not start M1 or claim that transparency, 60/120 Hz frame pacing, taskbar/fullscreen behavior, or cross-DPI behavior has passed. Detailed results and proposed falsification criteria are in [M0 report](../dock-evidence/M0/M0-report.md) and [route comparison](../tech-spike-comparison.md).
