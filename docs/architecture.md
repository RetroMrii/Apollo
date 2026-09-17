# Apollo architecture

## Status

Phase 9 adds the final application identity and distribution path: a custom dark-mode icon, a self-contained single-file Windows x64 portable build, and a non-admin per-user installer with package checksums.

## Decisions

### Runtime and UI

Apollo targets .NET 10 and WPF on Windows. .NET 10 is the current supported LTS available in the development environment. The application remains Windows-specific so process, startup, and notification-area integration can use direct Windows APIs where that improves reliability or efficiency.

### Solution boundaries

`Apollo.App` is the executable and WPF composition root. It owns views, view models, window lifetime, tray integration, and Windows-specific adapters.

`Apollo.Core` is platform-neutral and has no WPF dependency. It will own configuration models and the gaming-session state machine. Keeping deterministic lifecycle policy here makes the most failure-prone behavior inexpensive to test without creating a framework-heavy architecture.

The app references the core; the core never references the app. Services will be concrete by default. Interfaces will be introduced only at operating-system boundaries where a test seam is useful.

### Process monitoring

Apollo uses conservative process-name sampling on a background asynchronous loop with a two-second interval. Enabling monitoring performs an immediate snapshot, including during startup reconciliation. Each snapshot enumerates process names once, compares them against a precomputed case-insensitive target set, and disposes every `Process` object promptly. Unchanged active-game sets produce no UI notification. Disabling monitoring cancels and disposes the timer, waits for an in-flight scan, and rejects results from obsolete monitor generations so stale activity cannot reappear after shutdown. A system-wide enumeration failure skips the sample and preserves the last known state instead of falsely reporting that every game exited. WMI and ETW remain deferred because they add lifecycle complexity and do not yet demonstrate a reliability or resource advantage for this utility.

### State ownership

`RecorderLifecycleCoordinator` in `Apollo.Core` owns the active-game set, recorder-observed state, one launch attempt per session, manual-close suppression, and final-game shutdown debounce. Every process snapshot includes recorder presence so a recorder that disappears during an otherwise unchanged game session is detected. The WPF view model observes coordinator status and does not implement lifecycle policy.

The Windows adapter rechecks the configured process name immediately before launch, starts only the explicitly configured executable path, and terminates matching configured recorder processes without shell command construction. A recorder that exits between enumeration and termination is treated as successfully stopped while access and permission failures remain visible. V1 deliberately has no recorder-ownership distinction: a recorder that was already running is managed by the same final-game shutdown rule.

When the active-game set becomes empty, a cancelable five-second delay starts. A returning game cancels it. After the delay, the coordinator rechecks its active set and queries recorder presence immediately before termination. Turning monitoring off resets session state without closing the recorder.

### Dependencies

The application has no third-party runtime packages. MSTest is a development-only dependency for the core test project. Later runtime dependencies require a concrete Windows integration benefit. Styling uses local WPF resources rather than a UI framework.

### Configuration persistence

Configuration is stored as JSON in `%LOCALAPPDATA%\Apollo\settings.json`. Writes are serialized, written through to a uniquely named temporary file in the same directory, and atomically replace the prior file. Missing, malformed, partially outdated, commented, and trailing-comma configuration files load with safe defaults and normalization.

### Running-application picker and icons

The running-application catalog exists only while the Add Game dialog is open and is enumerated only when the user selects that mode or requests a refresh. It considers visible top-level applications, excludes Apollo, deduplicates multiple instances by case-insensitive process name, and disposes every `Process` object after the snapshot. Executable paths, version metadata, and icons are best-effort; access-denied processes remain selectable using their process name and window title.

Icons are extracted through the Windows Shell API without a UI-framework dependency. Shell icon handles are immediately converted to frozen WPF bitmap sources and released. Configured recorder/game icons live only with their corresponding view models, while picker icons live only for the modal dialog lifetime.

### Tray lifetime

WPF remains the primary UI. The built-in Windows Forms `NotifyIcon` supplies the notification-area integration without a third-party runtime package. The application uses explicit shutdown mode: the window close button hides the dashboard while monitoring continues, and the tray Exit command asynchronously stops monitoring and lifecycle work before shutdown. The icon and native menu are disposed on every exit path. Start minimized suppresses the initial window without creating a background catalog or additional worker.

### Windows startup

Startup registration uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, which is per-user, immediate, reversible, and requires no administrator privileges. Apollo owns only the `Apollo` value. The command contains the fully qualified executable path wrapped in quotes. The JSON setting is the desired state; startup reconciliation creates, updates, or removes the value on launch, and interactive changes compensate back to the last applied state if configuration persistence fails.

### Reliability and resource observations

The Phase 8 suite covers 100 monitor start/stop cycles, 50 complete recorder sessions, rapid debounce cancellation, disposal during a pending recorder close, transient snapshot failure, real process enumeration, and temporary Registry Run integration. Process adapters dispose every enumerated `Process` object, and test-created registry values are unique and removed in `finally` blocks.

On the development machine, a warmed Apollo instance hidden in the tray with monitoring enabled consumed 0.0625 CPU seconds during a 15-second sample. Private memory was 74.7 MB, within the preferred range; working set was 140.1 MB, above the soft target, with 25 threads and 724 handles. The difference is primarily shared desktop-framework pages rather than private committed memory. Artificial working-set trimming is intentionally avoided because it would trade a cosmetic metric improvement for paging and latency.

### Packaging

The application icon is an original dark-mode geometric mark supplied as a transparent PNG source and a generated multi-resolution ICO containing 16 through 256 pixel frames. The ICO is embedded into the executable and is also used by the tray and installer.

Release packaging publishes a self-contained, untrimmed `win-x64` single-file executable. Trimming remains disabled because WPF relies heavily on reflection and the reliability benefit outweighs a smaller binary. The portable artifact is a ZIP with no installation requirement. The Inno Setup installer installs under the current user's local application data, creates Start-menu integration, offers an optional desktop shortcut, and does not request elevation. SHA-256 checksums accompany both packages.

## Adapted delivery phases

1. Technical foundation: solution, WPF host, core boundary, build configuration, and this decision record.
2. Compact dark UI shell: dashboard, truthful empty states, view models, and non-functional settings presentation only where clearly disabled or labeled.
3. Configuration: resilient JSON persistence, atomic writes, recorder/game dialogs, executable selection, and focused core tests.
4. Monitoring and lifecycle: conservative process sampling, session state machine, duplicate prevention, manual-close suppression, and five-second shutdown debounce with tests.
5. Running-application picker: on-demand enumeration and best-effort metadata/icon extraction.
6. Tray lifecycle: close-to-tray, explicit exit, tray state controls, and start minimized.
7. Windows startup: clean, reversible per-user Registry Run registration without elevation.
8. Reliability and resource pass: transition stress tests, inaccessible processes, idle CPU/working-set measurement, and cleanup verification.
9. Portable packaging, per-user installer packaging, application identity, and final Windows smoke tests.

## Current risks

- Medal may use a launcher or helper process whose real identity differs from the selected executable. V1 will not expand process identity tracking without observed evidence.
- The WPF/Windows Forms working set remains above the preferred soft target even though private memory is within range; packaging tests should remeasure this on a clean Windows session.
- Enterprise policy or registry permissions can block Run-key changes; Apollo reports the failure and restores the last applied UI state.
- If a portable Apollo executable is moved while its prior location is unavailable, the Run entry cannot repair itself until Apollo is launched manually from the new location.
- Release packages are currently unsigned and Windows may show an unknown-publisher warning until a code-signing certificate is introduced.
- Phase 9 publishes Windows x64 only; ARM64 is not yet built or tested.
