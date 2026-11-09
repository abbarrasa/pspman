# Plan 004 — Device detection and mounting

Status: approved
Revision: 0.1
Specification: approved revision 0.3 of [spec.md](./spec.md)

## Scope and baseline

Implement device discovery, automatic mounting, session associations and safe removal on Linux and Windows. No copying, game-file probes, archive access, catalog migration or persistent device preferences. Existing CRUD, CSV contracts, GameMedium values and duplicate identity remain unchanged. All proposed files below are relative to the repository root; they are not implementation already present.

The current application composes catalog services in `CatalogStartup`, constructs `MainWindow` in `App`, and keeps catalog selection in `MainViewModel`. There are no device adapters. Existing xUnit suites cover Core, Infraestructure and Desktop; Desktop already has Avalonia headless infrastructure and typed bindings. Reuse those projects and conventions.

## Boundaries and data flow

1. Platform adapters expose physical devices, media generations, volumes, USB descriptors, observed roots, capabilities and native errors. OS handles and API calls remain in Infraestructure. (FR-1–FR-6, FR-14–FR-16, FR-18–FR-19)
2. Core applies pure classification, candidate, preference, identity-recovery and root-relative path rules. A small asynchronous session coordinator invokes adapter operations and publishes immutable session snapshots. It never references Avalonia, SQLite, D-Bus or Win32. (FR-7–FR-14, FR-16, FR-18)
3. Desktop translates snapshots into device rows and commands on its UI context. It supplies selected game ID, Medium and Location to the association rules. Catalog storage is not used for device choices. (FR-5, FR-10, FR-12, FR-17)
4. Startup selects the Linux or Windows adapter explicitly; future platform support replaces that factory choice without changing association rules. A provider collection permits a later network-source provider without rewriting current providers. No macOS/network provider or plugin discovery framework is added. (NFR-1–NFR-2)

### Session model

Use opaque provider-scoped device and volume keys, separate from display labels and current roots. Include identity confidence, media generation, source-group capabilities, destination capability, native operation capabilities, pending/error state and per-volume observations. Labels, VID/PID alone, ports, drive letters and mount paths are not unique reconnection identity.

Retain one optical source selection and one shared USB/Memory Card source selection, including the selected volume for that device; retain destination device keys by game ID. A disconnected explicit preference stays unavailable and is never replaced by a remaining candidate. Reliable reconnection recovers its current roots. A changed optical disc/card is a new media generation, not proof that the old medium returned. When reliable identity is absent or duplicated, leave associations unavailable for explicit reselection. New application execution starts with an empty preference store and no removal suppression. (FR-6–FR-16, FR-18)

## Files and responsibilities

### Core — create under `src/PSPMan.Core/Devices/`

| File | Responsibility | Requirements |
| --- | --- | --- |
| `DeviceSnapshot.cs` | Immutable physical-device, volume, capability, identity-confidence and error/result records; no platform handles. Keep source capabilities separate from informative medium labels. | FR-2, FR-5–FR-6, FR-14, FR-18–FR-19 |
| `IDeviceProvider.cs` | Async discovery/change subscription, volume mount and whole-device safe-removal contract; cancellation and explicit operation results. Snapshot revisions prevent stale operation results. | FR-1, FR-3–FR-4, FR-14–FR-16 |
| `DeviceAssociationRules.cs` | Pure candidate grouping, automatic unique-source resolution, collision results, per-group preference replacement, per-game PSP association and volume selection. | FR-7–FR-13, FR-18–FR-19 |
| `DeviceSessionState.cs` | Immutable session preferences, removal suppression, operation attempts and recovery state; no persistence. | FR-10, FR-12–FR-16, FR-18 |
| `DeviceSessionService.cs` | Coordinate providers, initial scan/change reconciliation, automatic mount eligibility, explicit retry and safe removal; publish complete snapshots. | FR-1, FR-3–FR-6, FR-13–FR-16, FR-18 |
| `MountedGamePathRules.cs` | Pure joining from explicit root syntax and valid stored Location; never check file existence or rewrite Location. | FR-6–FR-8, FR-11 |

`GameRecord`, `GameRecordRules`, repositories and CSV schema require no changes. New device eligibility must not reuse catalog duplicate comparison as a source-selection rule.

### Infrastructure — create under `src/PSPMan.Infraestructure/Devices/`

| File | Responsibility | Requirements |
| --- | --- | --- |
| `DeviceProviderFactory.cs` | Choose supported platform adapter and accept an explicit provider list for composition/tests. Report unsupported platform/service availability without loading foreign native libraries. | NFR-1–NFR-2 |
| `PspUsbRecognition.cs` | Explicit USB storage recognition rules and descriptor fixtures for PSP model families; reject label/directory heuristics. Track provenance and tested presentations alongside recognition data. | FR-2 |
| `Linux/LinuxDeviceProvider.cs` | Normalize UDisks2 inventory and change signals, reconcile physical parent/volume/media identities, obtain USB descriptor ancestry, mount volumes and remove a device. | FR-1–FR-6, FR-13–FR-16, FR-18–FR-19 |
| `Linux/IUDisksClient.cs`, `Linux/UDisksClient.cs` | Narrow typed D-Bus transport boundary for ObjectManager, properties, filesystem mount/unmount and drive eject/power-off; preserve native error names/messages. | FR-3–FR-5, FR-14–FR-16 |
| `Linux/LinuxUsbMetadataReader.cs` | Read USB parent descriptors from sysfs using OS-provided device ancestry, without scanning game directories. | FR-2, FR-19 |
| `Windows/WindowsDeviceProvider.cs` | Normalize PnP physical parents, storage/volume inventory, media changes, current roots and notifications; coordinate mount and safe removal. | FR-1–FR-6, FR-13–FR-16, FR-18–FR-19 |
| `Windows/IWindowsDeviceApi.cs`, `Windows/WindowsDeviceApi.cs` | Small testable Win32 boundary: Configuration Manager/SetupAPI, volume enumeration, storage queries, mount points and native veto/errors; dispose handles/registrations. | FR-1–FR-6, FR-14–FR-16, FR-18 |
| `Windows/WindowsDeviceAuthorization.cs` | Launch the fixed PSPMan elevated operation mode through ShellExecuteEx/runas only after a privilege failure; cancellation/result handling and authenticated bounded IPC. | FR-4, FR-15, NFR-5 |
| `Windows/WindowsDeviceOperationHost.cs` | Validate a fixed mount/remove request, re-enumerate target identity, reject unsupported/internal targets, perform one operation, return its native result and exit. No general command execution or catalog access. | FR-3–FR-4, FR-15, NFR-5 |

Modify `src/PSPMan.Infraestructure/PSPMan.Infraestructure.csproj` only to add a pinned stable `Tmds.DBus.Protocol` dependency for the Linux D-Bus client. Confirm .NET 10 compatibility and maintenance/security at implementation time; no dependency is installed during planning. This is a current transport need, not a general service framework.

### Desktop — create or modify

| File | Action and responsibility | Requirements |
| --- | --- | --- |
| `src/PSPMan.Desktop/ViewModels/DevicesViewModel.cs` | Create device panel state, source/volume choices, retry/removal commands and snapshot projection. | FR-5, FR-9–FR-11, FR-14–FR-19 |
| `src/PSPMan.Desktop/ViewModels/DeviceItemViewModel.cs` | Create typed row state with physical identity, informative type, volumes, roots, pending/error text and operation availability. | FR-5, FR-15, FR-17–FR-19 |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.Devices.cs` | Create selected-game association/destination projection and notification hooks; do not place native operations in MainViewModel. | FR-7–FR-14, FR-17 |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.cs` | Modify optional device-VM injection and selected-game notification; preserve existing constructor consumers and catalog commands. | FR-12, FR-17 |
| `src/PSPMan.Desktop/Views/DevicesView.axaml`, `DevicesView.axaml.cs` | Create typed device/source panel; code-behind only initializes view. No new ViewLocator navigation needed. | FR-5, FR-9–FR-11, FR-15, FR-17–FR-19 |
| `src/PSPMan.Desktop/Views/MainWindow.axaml` | Add bounded device panel and selected-game PSP selector without obscuring catalog/CSV flows. | FR-12, FR-17, NFR-3 |
| `src/PSPMan.Desktop/DeviceStartup.cs` | Create provider/session composition independently of catalog creation; return an observable unavailable service state on initialization failure. | FR-1, FR-5, NFR-1–NFR-3 |
| `src/PSPMan.Desktop/App.axaml.cs` | Compose devices, start after UI initialization, cancel/dispose discovery on exit. Preserve catalog startup failure behavior. | FR-1, FR-3, FR-13–FR-14 |
| `src/PSPMan.Desktop/Program.cs` | Dispatch only the validated Windows elevated-operation mode before Avalonia initialization; ordinary launch remains unchanged. | FR-4, NFR-5 |

Keep the application manifest at ordinary user privilege. No new SQLite tables, Windows service, shell scripts or system policy changes are planned. Elevated mode uses the same packaged application binary; verify both apphost publishing and development launch paths explicitly.

### Tests and evidence — create

| File | Responsibility / requirements |
| --- | --- |
| `tests/PSPMan.Core.Tests/DeviceAssociationRulesTests.cs` | FR-7–FR-13, FR-18–FR-19; grouping, preferences and no fallback. |
| `tests/PSPMan.Core.Tests/MountedGamePathRulesTests.cs` | FR-6–FR-8, FR-11; Linux and Windows roots, full Location preservation and invalid host paths. |
| `tests/PSPMan.Core.Tests/DeviceSessionServiceTests.cs` | FR-1, FR-3–FR-6, FR-13–FR-16, FR-18; fake providers, notification races, operation ordering and recovery. |
| `tests/PSPMan.Infraestructure.Tests/PspUsbRecognitionTests.cs` | FR-2; positive model/storage fixtures and misleading/unknown devices. |
| `tests/PSPMan.Infraestructure.Tests/LinuxDeviceProviderTests.cs` | FR-1–FR-6, FR-13–FR-16, FR-18–FR-19; typed fake transport/sysfs fixtures and an isolated D-Bus integration fixture. |
| `tests/PSPMan.Infraestructure.Tests/WindowsDeviceProviderTests.cs` | Same adapter requirements using fake native API, plus Windows-only native inventory checks. |
| `tests/PSPMan.Infraestructure.Tests/WindowsDeviceAuthorizationTests.cs` | FR-4, FR-15, NFR-5; request validation, denied/cancelled UAC, peer/target mismatch and bounded IPC lifecycle. |
| `tests/PSPMan.Desktop.Tests/DevicesViewModelTests.cs` | FR-5, FR-9–FR-19, NFR-3; UI context, command availability and stale completion. |
| `tests/PSPMan.Desktop.Tests/DeviceMountingFlowTests.cs` | Headless controls for AC-2–AC-5, AC-8, keyboard operation, error/retry and independent catalog interaction. |
| `specs/004-device-mounting/manual-validation.md` | AC-1–AC-8 native Linux/Windows procedures, hardware/model matrix and actual evidence; unavailable checks remain pending. |
| `specs/004-device-mounting/coverage.md` | Owning-suite line/branch coverage, command/results and justified native/hardware gaps. |

Support fixtures may be nested in those tests; create extra shared fixture files only when reused. Existing catalog/startup/CRUD/import/export tests provide regression gates, with targeted additions to existing startup tests for composition.

## Pure logic and pseudocode

- `GetSourceGroup(GameMedium)` returns optical or shared removable; device capabilities determine eligibility independently of display classification. (FR-7, FR-19)
- `ResolveSource(game, snapshot, preferences)` returns unavailable, choose-device, choose-volume or selected device/volume; never performs I/O. (FR-7–FR-11, FR-18)
- `ChooseSource(state, group, device, volume)` replaces only that group's choice. `ChooseDestination(state, gameId, psp)` changes only that game's choice. (FR-10, FR-12–FR-13)
- `ReconcileAssociations(state, snapshot)` preserves unavailable explicit selections, recovers only unique reliable device/media/volume identities and drops stale roots. (FR-6, FR-13–FR-14)
- `CanAutoMount(state, device, volume)` excludes mounted, pending, failed-until-explicit-retry and explicitly removed media until reconnect/new execution. (FR-3–FR-4, FR-16)
- `ResolvePath(root, location, syntax)` returns constructed path or a validation error, with no filesystem existence checks. Platform-invalid names fail without changing stored Location. (FR-6–FR-8, FR-11)

```text
resolve source(game, state):
    group = source group for game.Medium
    if explicit preference exists:
        locate its uniquely recognized device and media generation
        if unavailable: return error; do not consider alternative devices
    else:
        candidates = distinct mounted source devices offering group
        zero candidates -> unavailable
        multiple candidates -> request device selection
        one candidate -> use candidate
    if remembered selected volume exists:
        resolve its current identity/root; unavailable -> error, no fallback
    else:
        one mounted volume -> use it
        multiple mounted volumes -> request volume selection
    return device, volume, construct path(root, full stored Location)
```

```text
reconcile observation:
    subscribe before initial inventory; buffer/reconcile concurrent notifications
    normalize physical devices, media generations and volumes
    refresh roots and availability; publish immutable revision
    recover only unique reliable session identities
    for each eligible unmounted volume:
        queue one mount per volume; serialize operations on its physical device
        publish pending; invoke provider with native authorization allowed
        re-observe actual state after completion
        reject stale completion if device/media generation changed
        denial/error -> publish error and suppress automatic retry of that attempt
```

```text
safe remove(device):
    mark device pending; prevent concurrent mounts on its volumes
    request ordinary safe unmount of all mounted volumes, never force
    if any fails: stop removal, refresh all states, publish native failure
    otherwise request platform eject/power-off where supported
    refresh state; only confirmed success suppresses automatic remount
    partial removal is not success and never triggers forced cleanup
```

```text
construct path(root, location, syntax):
    validate medium-rooted slash Location using existing validation rules
    retain every segment after the single leading medium-root slash
    reject segments that cannot be represented on target host; never sanitize silently
    join segments below the complete trusted observed volume root
    verify lexical result remains below root; preserve case and stored Location
    return constructed path without opening/statting the game file
```

A constructed path is preparatory information, not a copy guarantee or symlink/content validation. Copy-time file access and destination-subdirectory policy remain outside this feature.

## Platform decisions and rejected alternatives

### Linux (FR-1–FR-6, FR-14–FR-16, FR-18–FR-19)

Use UDisks2 on the system D-Bus: ObjectManager inventory/signals, Filesystem.Mount/Unmount, Drive.Eject for optical media and supported Drive.PowerOff for removable physical devices. Obtain fresh MountPoints after each action, including partial failure; map shared physical parents so a request cannot silently eject an unrelated device. Read USB ancestry/VID/PID/interface/serial metadata from sysfs where exposed. Polkit owns authorization and its native agent; a missing service/agent is an explicit failure, not a password entry form.

Propose `Tmds.DBus.Protocol` for typed messages/signals. It avoids implementing a D-Bus transport or parsing localized command output; confine it to Infraestructure. Rejected: `sudo mount`, invoking a shell or permanently weakening Polkit rules. Rejected: repeated `lsblk`/`udisksctl` subprocess parsing as the primary event/identity protocol. UDisks2 and a functioning native authorization agent are runtime prerequisites; document service absence as unavailable rather than claim all Linux installations provide them.

### Windows (FR-1–FR-6, FR-14–FR-16, FR-18–FR-19)

Use Configuration Manager/SetupAPI inventory and notifications, USB parent descriptors, storage properties, volume GUID paths and current mount paths. Register notifications before initial enumeration and schedule asynchronous reconciliation outside the native callback. Reuse existing drive roots. For an otherwise accessible volume without a mount path, assign an available drive letter through SetVolumeMountPoint; handle races by re-enumeration rather than overwriting an occupied mapping. Do not enable system-wide automount or bring internal/offline policy-protected disks online.

Safe removal targets the removable physical device node with Configuration Manager veto reporting; optical media use the appropriate native media-eject path without removing the optical drive itself. Preserve native veto type/name, error codes and actual volume states. Do not force dismount or disconnect sibling devices that are not part of the selected physical device.

Privilege failures launch a single fixed-purpose operation through ShellExecuteEx with `runas`; the normal UI stays unelevated. Use a random per-request pipe with explicit initiating/elevated peer validation, bounded structured mount/remove messages, target re-enumeration and no arbitrary command/path execution. Do not rely on caller-supplied identifiers alone. The initiating UI re-observes native state rather than trusting a success message. Cancellation or IPC failure produces an error and no prompt loop. Do not retain credentials. Rejected: running the whole desktop as administrator, diskpart/PowerShell scripts as a device protocol, or a persistent privileged service.

### Recognition and identity (FR-2, FR-6, FR-13–FR-14)

Recognize PSP USB storage presentations, not directories or user-visible labels. Maintain a provenance-backed descriptor matrix covering model families and alternative storage presentations; do not invent VID/PID values or assert that one tested console certifies every model. VID/PID indicate device class/model compatibility, not unique device identity. USB serial plus OS-supported persistent device/volume identity can establish reliable reconnection when unique; absent/duplicated identity cannot justify automatic reassociation. Reader identity alone cannot establish that the same inserted card returned.

All-model support remains an acceptance requirement, with hardware gaps explicitly pending. Before implementing recognition, collect authoritative descriptors or sanitized real-device fixtures and document coverage; if a model cannot be identified from the available USB information, report that feasibility conflict for a specification decision rather than silently narrowing support. Rejected: choosing a PSP by drive letter, Sony vendor ID alone or `PSP` folder existence.

### Extensibility and performance (NFR-2–NFR-3)

Keep provider identity/capabilities separate from GameMedium and platform choice; future network providers can offer an existing source-group capability without modifying catalog identity. Additional catalog media, if needed later, are a separate domain change. A future macOS provider implements the same observation/operation contract. Verify these seams with an in-memory third provider, not an unused real provider or reflection plugin framework.

Use async D-Bus/I/O, worker execution for blocking native calls, immutable snapshots, coalesced notification bursts and per-device operation serialization. Serialize authorization prompts across devices to avoid simultaneous dialogues while leaving catalog viewing interactive. Avoid directory scans, game-file probes, recurring full-volume scans and keeping game files open. Dispose subscriptions and native handles at shutdown; late completion cannot mutate a disposed view model.

## Interface (FR-5, FR-9–FR-12, FR-14–FR-19, NFR-3–NFR-4)

Use a bounded scrollable device panel alongside the catalog, with physical-device rows and nested mounted volumes:

- **Game sources:** independent `CD/DVD source` and `USB / Memory Card source` selectors. More than one candidate leaves `Choose source` visible; selected multi-volume devices expose `Source volume`. Labels are informative; roots remain visible even when labels are missing/duplicated.
- **PSP destinations:** list recognized consoles separately. The selected game's details show a PSP selector; changing game selection restores that game's session choice. With one PSP, show the single available destination without a false collision. No destination subdirectory or Copy button is introduced.
- Rows show device name/type, each volume's root, `Detecting`, `Mounting`, `Ready`, `Removing`, `Unavailable` or an error with `Retry`. Device-level `Safely remove`/`Eject` acts on the entire device; pending operation disables conflicting row actions only.
- Show a constructed source path as preparatory information without asserting file existence. Missing device/volume/PSP has explicit unavailable feedback and never switches silently.
- English visible labels, accessible control names, keyboard focus and no color-only status. Native authorization follows OS locale. Explicit IsEnabled bindings stay aligned with command predicates. Preserve typed DataTemplates and bindings.

## Verification strategy and traceability

For every implementation increment, write behavioral tests first, demonstrate the expected failure, implement minimally, run relevant checks and stop at the authorized task boundary. Do not generate tasks or begin implementation until separately approved.

| Requirements | Planned verification |
| --- | --- |
| FR-1, FR-3 | Session fake-provider initial/hotplug races and duplicate events; adapter integration; real startup/hot insertion and already-mounted reuse on both OSes. |
| FR-2 | USB recognition fixtures for all supported model presentations; misleading folders/labels and unknown descriptors rejected; actual model matrix remains separate evidence. |
| FR-4 | Allow/deny/cancel/service-unavailable, serialized prompts, explicit retry; real Polkit/UAC evidence and elevated-host validation tests. |
| FR-5, FR-17 | Typed-binding/headless empty, pending, ready, collision, partial failure and retry states; native feedback and keyboard checks. |
| FR-6–FR-8 | Linux/Windows pure path matrix, case preservation, rooted Location not discarding root, invalid host path error, unique/no source and no file probes. |
| FR-9–FR-11 | Independent optical/shared removable choices; USB/card interoperability; selected unavailable source never falls back; no existence-based filtering. |
| FR-12 | Two games choosing different PSPs; one/zero PSP states; source preferences unaffected. |
| FR-13–FR-14 | Same-device changed-root recovery; changed media, reused drive letters, missing/duplicate serials, stale results, unavailable associations; restart clears state. |
| FR-15–FR-16 | Whole-device/multiple-volume removal, native busy veto, partial unmount, no force, suppression until reconnect, restart re-enables mount. |
| FR-18–FR-19 | All-volume discovery/mount, single volume automatic, multiple volumes explicit selection, duplicate/absent labels and ambiguous-reader informative fallback. |
| NFR-1 | Linux and Windows native integration/manual runs; platform-guarded tests explicitly reported as unexecuted when unavailable. |
| NFR-2 | Fake additional provider and platform factory substitution without changes to existing association rules; review dependency direction. |
| NFR-3 | Hold fake/native operation pending while browsing catalog and processing UI input; burst/coalescing/serialized-operation tests and handle/subscription lifecycle checks. |
| NFR-4 | English product states and documentation; host path syntax and native locale observed separately. |
| NFR-5 | Spies reject game-file I/O, catalog writes, format/repair/force methods and credential handling; review fixed elevated operations. |

Adapter mocks are unit/contract tests, not proof of OS integration. The Linux integration fixture should exercise the real D-Bus transport against an isolated fake UDisks service, never real disks in ordinary test runs. Windows integration exercises native read-only enumeration and notification registration on Windows; mutating native tests require explicitly selected disposable hardware. Hardware-sensitive procedures never target an arbitrary discovered device.

Build and regression commands from the repository root: `dotnet build PSPMan.slnx`, `dotnet test PSPMan.slnx`; collect coverage with the existing coverlet collector and record line/branch results by owning suite. Run Desktop when a graphical session is available. Add no test platform/framework. Report actual commands and results; this planning step runs no implementation tests.

Manual matrix: Linux and Windows; supported PSP families/storage presentations; CD/DVD, USB and memory-card readers (USB/internal where available); multiple volumes; two PSPs; native authorization allow/deny; busy-volume eject; disconnect/reconnect with changed roots; restart; no media; unsupported filesystem; read-only/unavailable device feedback. File-missing/access-at-copy errors remain future-copy acceptance, not a fabricated demo in this feature. A mounted read-only destination must not be advertised as verified writable for a future transfer.

## Documentation, risks and approval boundaries

- `AGENTS.md`: no changes proposed; current Core/platform boundaries, English artifacts, branch rules and verification instructions apply.
- `MEMORY.md`: record spec 0.3 approval and draft plan 0.1, then update at approved phase/task boundaries. Do not record USB serials or transient machine identifiers.
- Significant risks: incomplete PSP descriptor/hardware evidence; OS service availability; USB readers with weak identity; multi-drive physical removal; Windows UAC peer validation; mount-letter races and partial failures. Their native tests are required evidence, not optional polish.
- The plan does not change approved behavior. If descriptor feasibility or native removal/auth behavior makes a requirement impossible, stop affected implementation and return to clarification.
- Plan approval authorizes task decomposition only. A task list and each implementation step require their own authorization. Preserve local changes; branch per authorized feature task from current local dev; no commits/pushes.

## Primary technical references

- [UDisks2 Filesystem methods/properties](https://storaged.org/doc/udisks2-api/latest/gdbus-org.freedesktop.UDisks2.Filesystem.html) and [Drive eject/power-off/capabilities](https://storaged.org/doc/udisks2-api/latest/gdbus-org.freedesktop.UDisks2.Drive.html).
- [Tmds.DBus project](https://github.com/tmds/Tmds.DBus): proposed .NET D-Bus transport, confined to infrastructure.
- [Windows PnP notification registration](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_register_notification): subscribe before enumeration and do not block callbacks.
- [SetVolumeMountPointW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setvolumemountpointw), [CM_Request_Device_EjectW](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_request_device_ejectw) and [ShellExecuteEx request structure](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow).
