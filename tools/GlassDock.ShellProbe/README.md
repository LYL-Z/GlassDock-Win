# Windows Shell discovery probe

Run `./scripts/probe-windows-shell.ps1` from the repository checkout on Windows. The probe reads only the current-user and common Start Menu Programs known folders plus the Windows AppsFolder Shell namespace, resolves shortcut/AUMID identities and extracts up to 12 real Shell icons. It writes a sanitized summary and those PNG files under `.local/evidence/windows-shell-discovery/`, which is ignored by Git.

The probe does not launch or activate applications, enumerate application windows, inject input, or change system settings. It prints counts and an evidence path without printing installed application names or shortcut paths.

The catalog covers `.lnk` entries in those Start Menu folders and AUMID entries exposed by the Windows AppsFolder namespace. Packaged applications are identified by AUMID. Apps not exposed by those Shell surfaces, including apps without a Start Menu/Appsfolder entry, are outside this discovery scope; it is not a complete inventory of every installed package.

Shell icon bytes are extracted at runtime from Windows objects and are not bundled or redistributed. The existence of a local extracted image does not grant a right to redistribute that application's icon or brand assets.
