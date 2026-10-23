# Experimental Dock vertical slice

This is a replaceable WPF host that connects the existing Windows Shell provider, `DockApplicationSession`, Core state, and the configuration file store. It is a functional prototype, not a production window host or a macOS visual match.

## Build and run

From the repository root on D:, run:

```powershell
./scripts/build.ps1
./scripts/run-experimental-dock.ps1
```

Run the app only when you are ready for a visible desktop window. This prototype's app tiles issue real Shell launch/open commands when clicked. The current change has been built and tested without launching the UI or any real application.

The run script uses the repository-local .NET SDK when present and configures temporary files, NuGet packages, CLI state, and build output under `D:\MacOS\GlassDock-Win\.local`, `.tools`, and project `bin/obj` directories. The prototype stores its test profile in the ignored path `.local/experimental-dock`; it does not write to the normal user configuration location.

## Prototype behavior

- Application choices come from the existing current-user Windows Shell discovery adapter. Search and pin actions do not pin every discovered application.
- The default edge is Bottom. Bottom, Left, and Right can be selected and persisted.
- Dock section order is Pinned, Recent, Files. Recent items are shown only when Core has real observed activity; the UI does not synthesize recent usage. The file/folder section is reserved and does not yet provide a picker.
- Pinned app tiles show bounded PNG Shell icons when available. Decode failures use a clearly visible initials fallback and do not terminate the host.
- Context menu or Delete removes only the Dock reference. Mouse drag/drop and Ctrl+Arrow reorder within the same section. Enter/Space first asks the Shell provider to activate an existing app window, and launches only if no matching window is found; a denied foreground activation is returned without launching a second instance.
- A successful state change is saved before it is published by the session. Reopening the prototype loads the persisted state.

## Explicit limitations

- The backplate is a translucent WPF surface with a drop shadow. Cross-window desktop blur, liquid-glass optics, and final visual quality have not been demonstrated.
- This host does not implement an activation policy, click-through regions, taskbar negotiation, auto-hide, full-screen/game behavior, multi-monitor placement, or a verified no-focus startup. Window appearance and interactive behavior require a separately authorized desktop test.
- Launch/activate adapters are connected in code, but real launch, window activation, multiple-window choice, and focus behavior have not been exercised in this work.
- The picker bounds visible entries and icon resolution to keep Shell work finite. Its discovery list is refreshed when opened.
