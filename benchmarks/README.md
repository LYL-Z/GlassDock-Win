# Benchmarks

Run `scripts/measure-poc.ps1 -Candidate Wpf` or `-Candidate WinUI` after building. The script launches a topmost Dock for the requested sample duration, discards the first three seconds, records per-second CPU percentage normalized to logical processors, working set, private bytes, thread count, and handles, then closes only its own process. CSV files are written to ignored `.local/evidence/measurements/`.

The script does not measure GPU time, presentation cadence, animation frame duration, or 60/120 Hz behavior. For WinUI frame timing, use Windows Performance Recorder/WPA with the XAML Frame Analysis plugin where available; document the tool version and trace settings. A static idle baseline cannot establish animation smoothness.
