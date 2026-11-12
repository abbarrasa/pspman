# T44 — Application device lifetime

Status: approved; implementation authorized by the user on 2026-11-12
Basis: approved specification 0.3, plan 0.1 and task T44 in [tasks.md](./tasks.md).
Requirements: FR-1, FR-3, FR-13, FR-14, FR-16, NFR-3.

## Scope and implementation

1. Write lifecycle tests first using fake providers and the corrected asynchronous headless dispatcher. Observe missing lifetime behavior before implementing it.
2. Add a small Desktop application-device-lifetime owner around DeviceStartup. It owns cancellation, composition, the current DevicesViewModel and session cleanup, independently of catalog creation. Start composition/discovery asynchronously after UI initialization, with visible initialization/error feedback. Never block the UI on provider construction or discovery.
3. Wire that owner into [App](../../src/PSPMan.Desktop/App.axaml.cs). Keep the existing MainWindow/MainViewModel instance and any catalog selection/editor/import state when asynchronous device composition finishes. Add the smallest UI-context device-attachment notification seam to [MainViewModel.Devices](../../src/PSPMan.Desktop/ViewModels/MainViewModel.Devices.cs); extend [CatalogStartup](../../src/PSPMan.Desktop/CatalogStartup.cs) only as needed for initial device state and independent catalog failure handling. Preserve existing optional constructor consumers and compiled bindings.
4. On ordinary application shutdown, invalidate UI callbacks and cancel discovery/operations, then await session/provider/subscription disposal before completing the controlled exit. Handle shutdown during provider construction or initial inventory, duplicate shutdown requests and a late factory completion without attaching disposed state or leaking its returned resources. Do not use an unobserved async-void Exit handler or synchronously wait on UI-bound work.
5. Each execution creates a new session. Reuse existing mounts and allow automatic mounting again on a new execution; never persist source/volume/PSP preferences or removal suppression. Reuse existing Core/provider behavior rather than duplicating its rules.

Expected files: App, a new Desktop application-device-lifetime owner, DeviceStartup if lifecycle support is needed, MainViewModel.Devices, DevicesViewModel initialization feedback, and CatalogStartup only where required. Add lifecycle/integration tests in the existing Desktop suite. Update tasks and MEMORY on completion; no AGENTS changes or dependencies are proposed.

## Verification and boundaries

- Pending factory/inventory must not prevent catalog browsing or input; device-service failure must not disable a healthy catalog, and catalog failure must not prevent device startup.
- Verify initial devices are considered on both of two independent executions; explicit source/volume/per-game PSP choices reset and successful-removal suppression does not carry into the second execution. No redundant mount for already-mounted volumes.
- Verify cancellation during construction/discovery/operations, late observations, idempotent shutdown and disposal of fake native subscriptions/resources. Async headless assertions must execute before session disposal.
- Run root solution build/full regression and sequential focused coverage, reporting significant gaps. Validate startup/shutdown through injected providers rather than launching an application that could automatically mount arbitrary attached real devices. A native run requires an explicitly selected safe environment/device set; Windows/Polkit/UAC/hardware acceptance remains in the existing pending runbook.
- Keep T45–T53 outside this task. No copying, game-file access, media repair/formatting, persistent preferences, commits or pushes.

AUD-1 corrections and T44 implementation reside in `feature/004_T44_application-device-lifetime`. The user explicitly approved implementation after this plan was presented. Task evidence is recorded in [tasks.md](./tasks.md); final feature validation remains a separate phase.
