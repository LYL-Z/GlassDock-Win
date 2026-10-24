# Native icon resources (experimental)

GlassDock separates what a Dock item **looks like** from the Windows target it **operates on**. An icon override never changes the item's `DockTargetIdentity`, command route, folder location, or live state.

This is an experimental, local-only asset path for the replaceable WPF host. It does not bundle Apple artwork, download artwork, or imply that any Apple image is licensed for redistribution.

## Resolution order

For each Dock item the resolver checks, in order:

1. The specific Dock item ID.
2. Its scheme-aware Windows target identity.
3. Its semantic role (`Finder`, `SystemSettings`, `Downloads`, or `Trash`).
4. The Windows Shell icon provider.

Unavailable, unreadable, or undecodable custom artwork falls through to the Windows Shell icon. If that also fails, the experimental UI uses a name-derived text glyph. A directory refresh only changes the application catalog; it does not rewrite the icon manifest or persisted Dock order.

PNG and ICO files are read from the profile's `icon-assets` directory. The pure JSON codec is in `GlassDock.App`; filesystem access is isolated in `GlassDock.Persistence`. Files are bounded to 16 MiB and dimensions are bounded to 1024 pixels per side and 1,048,576 pixels total. The WPF host keeps image aspect ratio, keeps alpha, and chooses an ICO frame suitable for the current DPI; WPF's [IconBitmapDecoder](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.iconbitmapdecoder?view=windowsdesktop-10.0) exposes the ICO frames. It does not crop icons into a tile or add a per-icon plate. DPI-change cache invalidation and visual inspection on multiple monitors are not yet verified.

An icon mapping can provide theme (`Any`, `Light`, `Dark`) and visual-state (`Default`, `Empty`, `Full`, `Unknown`) variants. Explicit Light/Dark preferences select their matching variant and then `Any`; `FollowSystem` currently requests `Any` until the experimental host has a verified system-theme provider. Missing variants fall through to the Shell icon. Trash state is queried from Windows Shell; query failure is `Unknown`, never `Empty`.

## Local files and manifest

The experimental host's default profile is under `D:\MacOS\GlassDock-Win\.local\experimental-dock`. Add user-provided artwork manually under:

```text
.local/experimental-dock/icon-assets/assets/finder.png
.local/experimental-dock/icon-assets/assets/settings.png
.local/experimental-dock/icon-assets/assets/downloads.png
.local/experimental-dock/icon-assets/assets/trash-empty.png
```

Create `.local/experimental-dock/icon-assets/manifest.json` with entries such as the following. This is configuration only; the image files remain local and are not part of source control or a public build.

```json
{
  "schemaVersion": 1,
  "roleAssignments": [],
  "mappings": [
    {
      "selector": { "role": "Finder" },
      "variants": [{ "relativePath": "assets/finder.png", "theme": "Any", "state": "Default", "sourceNote": "user-provided Finder reference; provenance to be recorded locally", "redistributionStatus": "LocalUseOnly" }]
    },
    {
      "selector": { "role": "SystemSettings" },
      "variants": [{ "relativePath": "assets/settings.png", "theme": "Any", "state": "Default", "sourceNote": "user-provided Settings reference; provenance to be recorded locally", "redistributionStatus": "LocalUseOnly" }]
    },
    {
      "selector": { "role": "Downloads" },
      "variants": [{ "relativePath": "assets/downloads.png", "theme": "Any", "state": "Default", "sourceNote": "user-provided Downloads reference; provenance to be recorded locally", "redistributionStatus": "LocalUseOnly" }]
    },
    {
      "selector": { "role": "Trash" },
      "variants": [{ "relativePath": "assets/trash-empty.png", "theme": "Any", "state": "Empty", "sourceNote": "user-provided empty Trash reference; no full-state artwork supplied", "redistributionStatus": "LocalUseOnly" }]
    }
  ]
}
```

The built-in semantic mapping recognizes the Explorer target as `Finder`, the Windows Settings system-role identity as `SystemSettings`, the Downloads Known Folder as `Downloads`, and the Windows Recycle Bin system-role identity as `Trash`. The Downloads path is obtained from [`SHGetKnownFolderPath`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetknownfolderpath) using `FOLDERID_Downloads`; it is not synthesized from the profile path. Recycle Bin state is read with [`SHQueryRecycleBin`](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryrecyclebina), which reports the item count and supports querying all drives with an empty root-path string. An explicit `--preview-native-role-icons` opt-in seeds Finder, System Settings, Downloads, and Trash only into a unique `.local/evidence/experimental-preview/<run-id>/profile` folder; it cannot run against the normal profile or with Shell commands enabled. The empty Trash image is never selected for a non-empty or unknown Recycle Bin state.

For an app-specific icon, add a `roleAssignments` entry that assigns a role to an item ID or target identity, then map the role. For a one-off appearance that should not follow an app's identity, map the item ID directly. A target-specific mapping survives display-name changes and does not depend on a process name.

## Provenance and redistribution

`sourceNote` records the user's local source description. `redistributionStatus` records `Unverified`, `LocalUseOnly`, or `ClearedForRedistribution`; the field is provenance metadata, not a legal determination. A public asset may only be added after the rights holder and applicable license are verified. The four user-provided PNGs are now copied only to the Git-ignored `.local/experimental-dock/icon-assets` directory on the D: development machine. `source-record.json` stores their local source description, dimensions, and hashes. The images are not in tracked files, project resources, build inputs, or public packages; their redistribution rights are not cleared.

The bounded preview runner copies those local files and the manifest into its unique isolated evidence profile. It requires its own desktop-test authorization phrase, runs only in PreviewOnly, uses a five-minute watchdog, and captures only the Dock window. The normal build and automated tests do not read personal icon files. The app has no graphical icon-import editor.

## Implementation limits

- Product defaults remain unchanged: the default Dock state still contains Windows File Explorer and does not automatically pin Settings, Downloads, or Trash. The isolated icon-preview seed presents the four requested semantic roles without changing the ordinary profile. The visible preview is not yet verified on the desktop.
- The four PNG references supplied for this preview are RGB PNGs with no alpha channel or `tRNS` transparency chunk. The preview keeps their encoded pixels unchanged and does not synthesize transparency, so any white corners/background present in those source images will remain visible. A transparent high-resolution replacement is needed to assess the intended cutout silhouette faithfully.
- Downloads icon lookup obtains the actual Known Folder path through the Windows Known Folder API; opening Known Folder targets from a Dock command and Downloads stack previews are not implemented yet.
- Recycle Bin state lookup is read-only. Only an empty Trash reference image was provided; a full-state macOS image, emptying behavior, and confirmation flow are not implemented. In full/unknown state, the empty image is not used and the Shell fallback is requested.
- PreviewOnly blocks all Launch, Activate, and Open commands at the session-to-Shell boundary; these system-role items are visual previews until their command handlers are implemented and separately tested.
- The Windows Shell may supply a Windows-native fallback icon. It is not represented as a macOS icon.
- This does not complete Liquid Glass, animated magnification, or visual acceptance. Code, build, and automated test evidence do not establish actual desktop display or macOS visual parity.
