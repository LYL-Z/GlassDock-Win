# ADR 0001: UI and rendering stack

- Status: M0 gate blocked; no production decision
- Date: 2026-10-22

## Context

The first prototype needs an undecorated, translucent, edge-positioned Dock with eight placeholder icons. M0 compared WinUI 3 with Windows App SDK and WPF with Win32 interop using shared geometry and test content. Win32 + DirectComposition/Direct2D remains a possible minimal experiment only if both existing routes show material limitations.

## Decision

Not made. WPF currently leads only on the measured transparent-input requirement: in one Windows 11 environment reported at 250% DPI and 240Hz, a controlled lower Win32 window received synthetic SendInput single-click, double-click and drag-start messages through transparent regions on Bottom, Left and Right WPF docks. Dock content clicks and drags were received by WPF.

WinUI 3 did not pass the same transparent-corner test: its root received single and double pointer events at all three edges, while the underlying window received no clicks. WinUI content-area and drag cases were not completed because the test did not locate a reliable icon point.

One WGC frame confirms DWM-composed translucency and shadow for WPF/Light/Bottom/58 DIP; the checker pattern remained crisp and no background blur was observed. No paired WinUI, dark-theme, other-edge, DPI, or size captures exist. PresentMon ETW returned access denied, so there is no measured per-process frame-time result. A passive DwmGetCompositionTimingInfo probe now returns S_OK without an enabled administrator role and reports about 240Hz globally; it does not measure the Dock process or physical scanout. WinUI has a possible public SetWindowRgn candidate for rounded-window hit testing, but it has not been implemented or tested and may clip shadows/edges.

## Consequences

- M0 remains blocked; M1 does not start.
- Do not freeze WPF or WinUI as the production stack.
- WPF is a temporary input-behavior lead only.
- Do not claim 120Hz/240Hz animation performance, cross-DPI behavior, fullscreen/taskbar coexistence, or complete liquid-glass rendering.
- Revisit native Win32 + DirectComposition only after the existing candidates have evidence-backed limitations; do not infer superiority from lower-level APIs.
- Keep the repository's public visibility separate from the unselected project license. Do not change LICENSE until the owner decides.

## Evidence required before decision

- Reproducible WPF and WinUI real input receipt at content and transparent points, with user-approved interactive test conditions.
- DWM composed captures across both themes, multiple backgrounds, Dock edges and sizes; distinguish alpha translucency from actual blur/acrylic.
- 100/150/200/250% DPI, supported refresh modes, and multi-monitor movement where hardware permits.
- Normal maximized, borderless fullscreen, exclusive fullscreen when available, taskbar, focus changes, hide/show and recovery behavior.
- Per-process submission metrics and global DWM compositor timing kept as distinct measures; never label either panel scanout without an appropriate display-side measurement.
- Current-source build, unit tests and a clean clone of the exact tested revision; remote CI reported separately.

See [M0 report](../dock-evidence/M0/M0-report.md), [requirement matrix](../dock-evidence/M0/REQ-matrix.md) and [technology comparison](../tech-spike-comparison.md).
