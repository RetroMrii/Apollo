# Apollo

Apollo is a lightweight Windows utility that manages Medal around configured game sessions. Phase 9 adds a custom application identity, self-contained Windows x64 portable packaging, a non-admin per-user installer, checksums, and final distribution smoke tests.

## Requirements

- Windows 11
- .NET 10 SDK

## Build

```powershell
dotnet build Apollo.slnx
```

## Package

```powershell
.\packaging\Build-Packages.ps1
```

This produces a self-contained Windows x64 portable archive and, when Inno Setup 7 is installed, a non-admin per-user installer under `artifacts`. Package checksums are written to `artifacts\SHA256SUMS.txt`.

## Project structure

- `src/Apollo.App` — WPF composition root and desktop UI.
- `src/Apollo.Core` — UI-independent configuration and lifecycle logic.
- `tests/Apollo.App.Tests` — focused Windows integration tests.
- `tests/Apollo.Core.Tests` — configuration, monitoring, and recorder lifecycle tests.
- `docs/architecture.md` — architectural boundaries and incremental delivery plan.

Recorder selection, game add/remove/enable state, monitoring state, and lifecycle preferences persist under `%LOCALAPPDATA%\Apollo\settings.json`. Games can be added by browsing to an executable or selecting a currently open user application. Enabled games are detected by process name while monitoring is on. Apollo starts the recorder once per gaming session, respects a manual recorder close for the remainder of that session, and closes the recorder five seconds after the final game exits. Closing the window keeps Apollo and monitoring active in the notification area; double-clicking its icon restores the window, and the tray menu controls monitoring or exits Apollo. The optional Windows startup entry is stored under the current user and requires no administrator privileges.
