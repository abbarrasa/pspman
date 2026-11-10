# Tasks 004 — Device detection and mounting

Status: approved; execution requires individual task authorization
Revision: 0.1
Specification: approved [spec.md](./spec.md), revision 0.3
Plan: approved [plan.md](./plan.md), revision 0.1

## Scope and execution rules

- The user explicitly approved keeping one specification and exceeding the approximate ten-task target. All requirements and approved scope are preserved.
- Each item targets 20–30 minutes of focused work, including its focused tests. Estimates are not guarantees; if an item proves too large, stop and propose smaller subtasks before expanding it.
- Execute in the listed order; dependencies name the required prerequisites. Enabling work is not wired into production until complete, preserving the working catalog after every increment.
- For implementation tasks, write behavioral tests first, observe the expected failure, then implement and run the focused checks plus required build/regression checks. Evidence-only tasks record real observations instead of inventing a red/green cycle.
- Before code, present the individual task plan and wait for approval. Complete one task, record commands/results and covered FRs, update MEMORY.md, mark its checkbox only when its Done when condition holds, and stop.
- Follow repository branch rules: inspect Git, create feature/004_TNN_<brief-kebab-case-description> from current local dev only when safe, preserve pre-existing changes, and never commit/push as part of this flow.
- No copying, game-file probes, catalog migration, persistent preferences, macOS/network implementation or forced removal. USB descriptors used as fixtures must omit real unique identifiers.
- A passing fake-provider or headless test is not native-platform evidence. Missing Windows access, PSP models, authorization agents or disposable media keep the affected mandatory evidence tasks incomplete.
- First authorized implementation candidate: T01, after task-list approval and explicit authorization of its individual plan. No implementation is authorized by this document.

## Ordered tasks

- [x] **T01. Document the PSP USB recognition evidence matrix.** FR-2, NFR-1
  - Depends on: none.
  - Work: In manual-validation.md, list PSP model families/storage presentations, descriptor provenance and available versus missing evidence. Collect public descriptors or sanitized fixtures; never invent IDs or record unique serials.
  - Done when: Each model family has a documented evidence status and source; any recognition impossibility is raised before dependent recognition work, while unavailable physical checks remain explicitly pending.
  - Evidence: [Recognition matrix](./manual-validation.md), documentary research completed 2026-11-09. Sony manufacturer sources cover all five families; pinned hwdata/PSPLINK sources distinguish registry candidates from development mode. Full storage descriptors and all Linux/Windows hardware checks remain pending for T15/T49–T53; no impossibility was established. No product code or automated tests were written/run. Git whitespace checks passed; no commits/pushes.

- [x] **T02. Introduce device snapshots and the provider contract.** FR-1, FR-3, FR-5, FR-14, FR-18, NFR-2
  - Depends on: none.
  - Work: Create DeviceSnapshot and IDeviceProvider with immutable identities, volumes, capabilities, roots and operation outcomes. Add a fake-provider consumer test proving multiple volumes and missing roots can be represented without platform dependencies.
  - Done when: The consumer test passes with a fake provider; Core has no platform, Desktop or persistence reference and the solution builds.
  - Evidence: Individual T02 plan explicitly approved. Tests written first: filtered run initially failed compilation because the Devices namespace/contracts did not exist (CS0234/CS0246); after implementation, `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceProviderContractTests --nologo` passed 4/4. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 643/643 (Core 398, Infraestructure 70, Desktop 175), no skips. Immutable snapshots, provider-scoped keys, full inventory revisions, cancellation/disposal and volume-mount/whole-device-removal requests are defined in Core without new references. These are contract-consumption tests, not native mounting evidence. Branch: `feature/004_T02_add-device-provider-contract`; no commits/pushes.

- [x] **T03. Resolve source groups and zero/one/many candidates.** FR-7, FR-8, FR-9, FR-19
  - Depends on: T02.
  - Work: Add pure DeviceAssociationRules tests and logic for optical versus shared USB/card eligibility, PSP exclusion and distinct physical candidates; ignore DVD Number and file contents.
  - Done when: Tests return unavailable, selected or choose-device for zero/one/many candidates and show USB/card interoperability without game-file I/O.
  - Evidence: Individual T03 plan explicitly approved. Tests were written first; after adding signatures, the filtered run failed 29/29 with NotImplementedException, then passed 29/29 after implementing the rules. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceAssociationRulesTests --nologo`. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 672/672 (Core 427, Infraestructure 70, Desktop 175), no skips. Tests cover source groups, capability-based eligibility, PSP exclusion, provider-scoped deduplication, multi-volume devices, stale/missing roots and partially available devices. No game-file probes or DVD-number input. Preferences/volume choice remain later tasks. Branch: `feature/004_T03_resolve-source-candidates`; no commits/pushes.

- [x] **T04. Keep independent active source preferences.** FR-10, FR-11, FR-13
  - Depends on: T03.
  - Work: Add DeviceSessionState preference transitions: replace only the chosen group, retain an unavailable explicit choice, and prohibit fallback.
  - Done when: Changing USB/card source leaves optical preference unchanged, applies across games in that group, and an unavailable preference never selects another device.
  - Evidence: Individual T04 plan explicitly approved. Tests written first: with signatures only, the filtered run passed the 29 existing cases and failed the 14 new cases with NotImplementedException; after implementation all 43 passed. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceAssociationRulesTests --nologo`. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 686/686 (Core 441, Infraestructure 70, Desktop 175), no skips. DeviceSessionState is immutable and starts empty; pure transitions replace one group only. Resolution preserves unavailable explicit preferences without fallback, including missing/unmounted/incompatible devices and provider-key mismatches. Volume choice/reconnection recovery remain T05/T09; no persistence, native devices or UI changed. Branch: `feature/004_T04_keep-source-preferences`; no commits/pushes.

- [x] **T05. Select the source volume within a physical device.** FR-8, FR-11, FR-18
  - Depends on: T04.
  - Work: Add automatic single-volume resolution and explicit multi-volume selection, including absent/duplicate labels and an unavailable previously selected volume.
  - Done when: One mounted volume resolves automatically; multiple volumes require a session choice; label ambiguity does not block selection and a lost chosen volume reports unavailable.
  - Evidence: Individual T05 plan explicitly approved. Tests written first: 17 new cases failed with NotImplementedException while 43 existing cases passed; after implementing volume rules all 60 passed. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceAssociationRulesTests --nologo`. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 703/703 (Core 458, Infraestructure 70, Desktop 175), no skips. Coverage includes single/multiple available volumes, absent/duplicate labels, unavailable explicit volume without fallback, explicit replacement, independent group state, duplicate/foreign observations and current roots. Changing source clears only that group’s volume; reselecting the same source preserves it. Catalog Location, native adapters and UI are unchanged. Branch: `feature/004_T05_select-source-volume`; no commits/pushes.

- [x] **T06. Associate a PSP separately with each game.** FR-12, FR-13, FR-14
  - Depends on: T02.
  - Work: Add destination choices keyed by game ID, single/zero PSP outcomes and explicit unavailable associations.
  - Done when: Two games retain different PSP selections; changing one does not affect the other, and disconnection cannot redirect an explicit choice.
  - Evidence: Individual T06 plan explicitly approved. Tests written first: 18 new cases failed with NotImplementedException while 60 existing cases passed; after implementation all 78 association cases passed. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceAssociationRulesTests --nologo`. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 721/721 (Core 476, Infraestructure 70, Desktop 175), no skips. Immutable destination choices are keyed by game ID and preserve source/volume preferences. Tests cover independent replacement, zero/one/many PSPs, mounted-volume availability, physical-device deduplication, provider-scoped keys, new-session isolation and explicit unavailable choices without fallback. Native identity recovery, UI and copying remain out of this task. Branch: `feature/004_T06_assign-psp-per-game`; no commits/pushes.

- [x] **T07. Construct Linux paths from complete catalog locations.** FR-6, FR-7, FR-8, FR-11
  - Depends on: T02.
  - Work: Add pure MountedGamePathRules for slash roots and existing Location validation; preserve every directory and case without filesystem access.
  - Done when: /media/collection plus /GAMES/Game.rar yields /media/collection/GAMES/Game.rar; invalid traversal fails and no segment is treated as a label.
  - Evidence: Individual T07 plan explicitly approved. Tests written first: 32 cases failed with NotImplementedException after adding signatures, then passed after implementation. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~MountedGamePathRulesTests --nologo`. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 753/753 (Core 508, Infraestructure 70, Desktop 175), no skips. Pure Linux construction preserves the selected root, full Location, case, Unicode and spaces; root/trailing slash and invalid traversal cases are covered. The existing GameRecordRules.IsWellFormedLocation predicate was made reusable and null-safe, preserving catalog validation criteria rather than duplicating them. No filesystem access or catalog writes; Windows syntax remains T08. Branch: `feature/004_T07_construct-linux-game-paths`; no commits/pushes.

- [x] **T08. Construct Windows paths without discarding the selected root.** FR-6, FR-7, FR-11, NFR-4
  - Depends on: T07.
  - Work: Extend explicit path-syntax rules for drive roots and observed mounted-folder roots; reject Windows-invalid segments without sanitizing or changing catalog values.
  - Done when: Cross-platform tests preserve the chosen Windows root and case, reject unrepresentable names/traversal and never open or stat a game file.
  - Evidence: Individual T08 plan explicitly approved. Tests written first: the initial run failed 9 valid Windows constructions while 77 cases passed (32 Linux plus 45 Windows rejection cases); after implementation all 86 path cases passed, including 54 new Windows cases. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~MountedGamePathRulesTests --nologo`. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 807/807 (Core 562, Infraestructure 70, Desktop 175), no skips. Windows drive/mounted-folder roots, case/Unicode, invalid characters, reserved names/extensions/superscript digits, trailing dots/spaces and traversal are covered without filesystem probes or catalog changes. Naming restrictions checked against [Microsoft documentation](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file). All tests executed on Linux; native Windows checks remain pending. Branch: `feature/004_T08_construct-windows-game-paths`; no commits/pushes.

- [x] **T09. Recover session associations only from reliable identity.** FR-6, FR-13, FR-14
  - Depends on: T04, T05, T06.
  - Work: Add pure reconciliation for changed roots, unique device/media/volume identity, changed cards/discs, duplicated IDs and reused mount paths.
  - Done when: Reliable reconnection restores selections with fresh roots; ambiguous identities or different inserted media do not restore old associations; new session state is empty.
  - Evidence: Individual T09 plan explicitly approved. Tests written first: 15 new cases failed with NotImplementedException while the 78 existing association cases passed; implementation passed all 93. Two additional risk checks verified pending-volume reconnection/repeated reconnect and changed card identity without a media-generation token; final association suite passed 95/95. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceAssociationRulesTests --nologo`. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 824/824 (Core 579, Infraestructure 70, Desktop 175), no skips. Session evidence is independent per source group/game and excludes roots/labels; matching is provider-scoped and requires unique persistent identity for recovery, plus applicable medium/volume evidence. Unavailable choices stay explicit without fallback, including reused keys/roots and ambiguous IDs. Reconcile after choices against the current observation, then on every inventory including removals; native event wiring remains T10/later. Branch: `feature/004_T09_recover-device-associations`; no commits/pushes.

- [x] **T10. Reconcile startup inventory and hotplug observations.** FR-1, FR-5, FR-6, FR-14, NFR-3
  - Depends on: T02, T09.
  - Work: Introduce DeviceSessionService subscription-before-enumeration, immutable snapshot publication and duplicate-event reconciliation using controllable fake providers.
  - Done when: A device arriving during initial enumeration appears once; disconnect removes usable roots and no UI/platform dependency enters Core.
  - Evidence: Individual T10 plan explicitly approved. Eight tests were written first and failed with NotImplementedException. After implementation and correcting collection assertions to compare immutable-array contents, all eight passed; an additional observation-failure/late-initial-read regression passed too. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceSessionServiceTests --nologo` passed 9/9. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 833/833 (Core 588, Infraestructure 70, Desktop 175), no skips. DeviceSessionService starts change enumeration before initial reads, serializes immutable session publication, rejects stale/duplicate provider revisions, deduplicates devices, clears unusable roots and invokes T09 reconciliation. Tests cover insertion during a blocked initial read, provider isolation, disconnect/reconnect and observation disposal. Provider adapters remain responsible for native subscription-registration races under IDeviceProvider; no native mounting, UI or persistence is introduced. Branch: `feature/004_T10_observe-device-inventory`; no commits/pushes.

- [x] **T11. Automatically mount eligible volumes exactly once.** FR-3, FR-5, FR-18
  - Depends on: T10.
  - Work: Queue automatic mounts for unmounted volumes and reuse observed mounts; serialize operations on the same device and publish pending state.
  - Done when: Already-mounted volumes receive no mount call, all eligible volumes are considered and repeated notifications cannot start duplicate mounts.
  - Evidence: Individual T11 plan explicitly approved. Tests written first: 7 required mount scenarios failed by bounded timeout because T10 made no requests; 14 existing/ineligible cases passed. After implementation the service suite passed 21/21, including 12 new cases. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceSessionServiceTests --nologo`. `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 845/845 (Core 600, Infraestructure 70, Desktop 175), no skips. Automatic requests cover optical/USB/card/PSP capabilities, reuse observed mounts, serialize per physical device and track volume/media attempts without duplicate notifications starting extra requests. Pending state is published before dispatch; completion refreshes provider inventory and never invents a root. Requests use the current provider revision/media generation; disposal waits for cooperative mount cancellation. Native adapters remain pending. Branch: `feature/004_T11_automount-device-volumes`; no commits/pushes.

- [x] **T12. Handle native authorization failure and explicit retry.** FR-4, FR-5, NFR-3
  - Depends on: T11.
  - Work: Add denied/cancelled/error transitions, explicit retry and a coordinator preventing concurrent authorization dialogues across devices.
  - Done when: Denied/cancelled attempts show errors without automatic prompt loops; explicit retry works and unrelated snapshot/catalog work remains unblocked.
  - Evidence: Individual T12 plan explicitly approved. Tests were written first: the filtered run initially failed because `RetryMountAsync` did not exist; after implementation, `DeviceSessionServiceTests` passed 25/25. The service now preserves authorization-denied/cancelled errors, suppresses automatic retries until an explicit retry, serializes provider mount calls through one authorization gate and keeps association updates available while a mount is pending. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceSessionServiceTests --nologo`. Full verification: `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 849/849 (Core 604, Infraestructure 70, Desktop 175), no skips. Branch: `feature/004_T12_authorization-retry-coordination`; no commits/pushes.

- [x] **T13. Coordinate whole-device safe removal and suppression.** FR-14, FR-15, FR-16, FR-18
  - Depends on: T12.
  - Work: Add removal coordination, exclusion of conflicting mounts, actual-state reconciliation after partial failure and successful-removal suppression.
  - Done when: Fake-provider tests show no force, correct partial-error state, no immediate remount after success and renewed eligibility after reconnect or new execution.
  - Evidence: Individual T13 plan explicitly approved. Tests were written first: the filtered run initially failed because `RemoveDeviceAsync` did not exist; after implementation, `DeviceSessionServiceTests` passed 29/29. The service requests whole-device removal with the current revision/media generation, publishes `Removing`, rejects conflicts with active mounts, preserves partial native errors, reobserves state, suppresses automatic remount after success and clears suppression on disconnection or media-generation change. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceSessionServiceTests --nologo`. Full verification: `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 853/853 (Core 608, Infraestructure 70, Desktop 175), no skips. Branch: `feature/004_T13_safe-removal-suppression`; no commits/pushes.

- [x] **T14. Reject stale completions and dispose session subscriptions.** FR-13, FR-14, NFR-2, NFR-3
  - Depends on: T13.
  - Work: Guard results by device/media generation and session lifetime; test coalesced event bursts, cancellation, disposal and a third in-memory provider.
  - Done when: Late operations cannot revive a disconnected/replaced device or disposed session; subscriptions close and a third provider uses unchanged association rules.
  - Evidence: Individual T14 plan explicitly approved. Tests were written first: the filtered run initially exposed that a stale removal occupied a replacement generation; after implementation, `DeviceSessionServiceTests` passed 33/33. Accepted inventory revisions now invalidate active mount/removal work for unavailable or changed-generation devices, while late completions require the same active work reference and cannot clear replacement operations. Disposal cancellation closes provider subscriptions and three providers retain independent inventories. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~DeviceSessionServiceTests --nologo`. Full verification: `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 857/857 (Core 612, Infraestructure 70, Desktop 175), no skips. Branch: `feature/004_T14_stale-completions-disposal`; no commits/pushes.

- [x] **T15. Implement evidence-backed PSP USB recognition.** FR-2, NFR-5
  - Depends on: T01, T02.
  - Work: Create PspUsbRecognition and positive/negative descriptor fixtures from the evidence matrix. Keep VID/PID model recognition separate from unique identity and reject labels/folder heuristics.
  - Done when: Each implemented recognition rule has provenance and a passing fixture; misleading Sony/non-storage/unknown descriptors do not silently become PSP destinations; unsupported evidence remains disclosed.
  - Evidence: Individual T15 plan explicitly approved. Tests were written first: the filtered run initially failed because the recognition descriptors and recognizer did not exist; after implementation, `PspUsbRecognitionTests` passed 7/7. `PspUsbRecognition` recognizes only the evidence-backed `054c:01c8` and `054c:02d2` candidates when an accessible storage association and USB mass-storage interface are present, rejects the `054c:01c9` development protocol, and leaves unknown Sony devices, labels/directories and incomplete storage evidence inconclusive. Synthetic fixtures retain the T01 provenance references; no physical descriptor or exhaustive all-model certification is claimed. Command: `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~PspUsbRecognitionTests --nologo`. Full verification: `dotnet build PSPMan.slnx --nologo` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --nologo` passed 864/864 (Core 619, Infraestructure 70, Desktop 175), no skips. Branch: `feature/004_T15_psp-usb-recognition`; no commits/pushes.

- [x] **T16. Connect a typed Linux D-Bus client to inventory.** FR-1, FR-5, NFR-1
  - Depends on: T02.
  - Work: Pin the approved Tmds.DBus.Protocol transport after compatibility/security review; introduce IUDisksClient/UDisksClient ObjectManager inventory decoding and transport tests.
  - Done when: Inventory decodes typed drive/block/filesystem data; missing service and malformed replies produce explicit failures, with no localized command parsing.
  - Evidence: Individual T16 plan explicitly approved. Tmds.DBus.Protocol 0.94.1 is pinned in Infraestructure and the typed ObjectManager transport decodes UDisks2 object paths, interfaces, variants and byte/string-array properties without shell or localized command parsing. Fake-transport tests cover typed drive/block/filesystem inventory, unavailable service, malformed replies, cancellation and disposal: 4/4 passed. Full verification: `dotnet build PSPMan.slnx --no-restore` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --no-build --no-restore` passed 868/868 (Core 619, Infraestructure 74, Desktop 175), no skips. Branch: `feature/004_T16_linux-dbus-client`; no commits/pushes.

- [x] **T17. Normalize Linux physical devices and mounted volumes.** FR-3, FR-5, FR-6, FR-18, FR-19
  - Depends on: T16.
  - Work: Create LinuxDeviceProvider inventory mapping for parent drives, empty readers, optical media, volume labels, mount roots and source capabilities.
  - Done when: Fixtures produce one physical row with its volumes, preserve current roots, ignore internal non-target storage and classify ambiguous removable sources informatively as Memory Card.
  - Evidence: Individual T17 plan explicitly approved. LinuxDeviceProvider joins typed Block objects to removable Drive parents, keeps empty optical readers, preserves labels/device paths/mount roots, excludes non-removable internal drives and classifies unknown removable buses as Memory Card without changing source eligibility. T17 fixture tests passed 3/3. Full verification: `dotnet build PSPMan.slnx --no-restore` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --no-build --no-restore` passed 871/871 (Core 619, Infraestructure 77, Desktop 175), no skips. Branch: `feature/004_T17_linux-device-provider`; no commits/pushes.

- [x] **T18. Read Linux USB ancestry and persistent identity metadata.** FR-2, FR-13, FR-14, FR-19
  - Depends on: T15, T17.
  - Work: Implement LinuxUsbMetadataReader against injected sysfs roots; join USB descriptors to storage devices and retain identity confidence/media generation.
  - Done when: Fixtures cover PSP recognition, USB card readers, missing serials and changed media without using directory names or assuming reader identity identifies its card.
  - Evidence: Individual T18 plan explicitly approved. LinuxUsbMetadataReader reads VID/PID, USB mass-storage interfaces and reader serials from an injected sysfs ancestry, marks missing serials ambiguous, keeps media generations separate by observed block ancestry and never reads directory or volume labels as identity. Fixtures cover PSP recognition, a USB reader, missing serials and changed media: 3/3 passed. Full verification: `dotnet build PSPMan.slnx --no-restore` passed with 0 warnings/errors; `dotnet test PSPMan.slnx --no-build --no-restore` passed 874/874 (Core 619, Infraestructure 80, Desktop 175), no skips. Physical hardware and native sysfs integration remain pending for later acceptance. Branch: `feature/004_T18_linux-usb-metadata`; no commits/pushes.

- [ ] **T19. Observe Linux device and root changes.** FR-1, FR-5, FR-6, FR-14
  - Depends on: T18.
  - Work: Add UDisks object/property subscriptions, initial-scan race handling and subscription cleanup.
  - Done when: Transport/provider tests deliver insertion, removal and changed MountPoints without duplicate devices, stale roots or leaked subscriptions.

- [ ] **T20. Mount Linux volumes through native authorization.** FR-3, FR-4, FR-5, FR-18
  - Depends on: T19.
  - Work: Implement Filesystem.Mount and refresh actual mount state; preserve native denied/cancelled/service errors and allow Polkit interaction.
  - Done when: Transport assertions verify mount calls with native interaction allowed, reuse of existing mounts and accurate post-operation roots/errors without credentials or sudo.

- [ ] **T21. Safely unmount all Linux volumes without forcing.** FR-14, FR-15, FR-18
  - Depends on: T20.
  - Work: Implement ordinary per-volume Unmount sequencing and actual-state refresh for partial failure before drive removal.
  - Done when: A busy volume stops removal and returns its native error; already-unmounted volumes are not fabricated as mounted and no force option is sent.

- [ ] **T22. Complete Linux eject and physical-device power-off.** FR-15, FR-16
  - Depends on: T21.
  - Work: Use optical Eject or supported removable-drive PowerOff after safe unmount; map physical/sibling scope and confirm actual state.
  - Done when: Tests verify optical-drive retention, whole-device scope, rejected/unsupported removal errors and no removal of unrelated siblings or false success.

- [ ] **T23. Verify the real Linux D-Bus transport in isolation.** FR-1, FR-3, FR-4, FR-6, FR-14, FR-15, NFR-1
  - Depends on: T22.
  - Work: Add an isolated test-bus fixture with a fake UDisks service exercising real message serialization, signals, mount replies and native error mapping.
  - Done when: The integration fixture passes through the production transport without touching the host system bus or disks; service shutdown cleans all resources.

- [ ] **T24. Enumerate Windows physical storage parents.** FR-1, FR-5, FR-18, FR-19, NFR-1
  - Depends on: T02.
  - Work: Introduce IWindowsDeviceApi/WindowsDeviceApi and WindowsDeviceProvider read-only physical-device enumeration, parent relationships and capabilities.
  - Done when: Native-boundary tests distinguish supported removable/optical devices from internal targets and group multiple volumes under a physical device without loading Win32 on Linux.

- [ ] **T25. Map Windows volume identities and current roots.** FR-3, FR-5, FR-6, FR-18
  - Depends on: T24.
  - Work: Implement volume enumeration, GUID identities, current mount paths, labels and no-media/error outcomes.
  - Done when: Fixtures map drive and mounted-folder roots to the correct physical parent, preserve missing/duplicate labels and avoid treating drive letters as persistent identities.

- [ ] **T26. Map Windows PSP USB metadata and reconnection confidence.** FR-2, FR-13, FR-14, FR-19
  - Depends on: T15, T25.
  - Work: Walk PnP USB parent descriptors and integrate the shared recognizer; keep device, inserted-media and volume identities distinct.
  - Done when: Tests distinguish PSP from generic storage and reject automatic identity recovery for reused roots, missing/duplicate serials or changed reader media.

- [ ] **T27. Subscribe to Windows device and volume notifications.** FR-1, FR-5, FR-6, FR-14, NFR-3
  - Depends on: T26.
  - Work: Register Configuration Manager callbacks before inventory, schedule asynchronous reconciliation and release registrations/native handles.
  - Done when: Arrival-during-scan, root change, removal and disposal tests pass; callbacks do not block on inventory/mount work.

- [ ] **T28. Assign Windows roots to eligible unmounted volumes.** FR-3, FR-4, FR-6, FR-18
  - Depends on: T27.
  - Work: Reuse existing paths; assign an available drive letter only to eligible accessible removable volumes; map privilege errors and re-enumerate on letter conflicts.
  - Done when: Tests never overwrite an occupied root, change global automount policy or bring internal disks online; races produce refreshed state or a native error.

- [ ] **T29. Request Windows whole-device safe removal.** FR-14, FR-15, FR-16, FR-18
  - Depends on: T28.
  - Work: Implement ordinary volume preparation and physical-node safe-removal requests; preserve veto information and actual partial states without force.
  - Done when: Busy-volume/veto tests prevent success, no forced dismount occurs and the request targets the selected physical device rather than one arbitrary volume.

- [ ] **T30. Eject Windows optical media without removing its drive.** FR-15, FR-16
  - Depends on: T29.
  - Work: Add the native optical media-eject branch and error mapping while retaining the reader/drive inventory entry.
  - Done when: Tests distinguish optical media ejection from USB physical removal and retain accurate empty-drive state after success or rejection.

- [ ] **T31. Define the bounded Windows elevated-operation protocol.** FR-4, FR-15, NFR-5
  - Depends on: T28, T29.
  - Work: Define fixed mount/remove requests and bounded results in WindowsDeviceAuthorization/WindowsDeviceOperationHost; reject arbitrary commands, paths and malformed messages.
  - Done when: Protocol tests reject unknown operations, oversize/malformed input and internal/ineligible targets; no request can execute a shell command or access the catalog.

- [ ] **T32. Authenticate Windows operation IPC peers.** FR-4, NFR-5
  - Depends on: T31.
  - Work: Implement random per-request pipe setup, explicit access restrictions and initiating/elevated process validation, with bounded lifecycle and cleanup.
  - Done when: Tests reject wrong peer/process/request identities and disconnect/timeout paths close handles; authentication does not depend solely on an untrusted message token.

- [ ] **T33. Execute one validated elevated Windows operation.** FR-3, FR-4, FR-15, NFR-5
  - Depends on: T32.
  - Work: Implement operation-host target re-enumeration and one mount/remove request, then structured result and exit; reuse native operations without general privilege exposure.
  - Done when: Tests reject target replacement between request and execution, run exactly the authorized fixed operation and preserve native failures without accessing game files or credentials.

- [ ] **T34. Launch the Windows operation host with native UAC.** FR-4, FR-5, NFR-5
  - Depends on: T33.
  - Work: Implement ShellExecuteEx/runas launch only after privilege failure; capture cancellation/launch/IPC errors and refresh observed state independently of host success reports.
  - Done when: Tests show no whole-UI elevation, no launch on ordinary success and no prompt loop after denial; a successful host reply alone cannot fabricate a mounted/removed state.

- [ ] **T35. Dispatch elevated mode before Avalonia initialization.** FR-4, NFR-5
  - Depends on: T34.
  - Work: Add strict Program argument dispatch and preserve normal startup; validate packaged apphost versus development launch resolution without shell scripts.
  - Done when: Dispatch tests show ordinary launch follows Avalonia startup, invalid helper invocations fail closed and valid operation mode never starts the catalog/UI.

- [ ] **T36. Verify Windows native read-only integration.** FR-1, FR-5, FR-6, FR-14, NFR-1
  - Depends on: T30, T35.
  - Work: Add Windows-only native inventory/notification registration checks and explicit skip/report behavior on other platforms; no test mutates discovered hardware.
  - Done when: On Windows the checks pass and dispose handles; unavailable Windows execution is reported pending rather than counted as native verification.

- [ ] **T37. Compose platform providers independently of catalog startup.** FR-1, FR-5, NFR-1, NFR-2
  - Depends on: T14, T23, T36.
  - Work: Create DeviceProviderFactory and DeviceStartup with explicit platform selection, injectable provider list and observable unavailable-service results.
  - Done when: Tests choose only the host adapter, preserve unrelated catalog startup behavior and accept an extra in-memory provider without changing association logic.

- [ ] **T38. Project device and volume rows into a typed view model.** FR-5, FR-14, FR-17, FR-18, FR-19, NFR-4
  - Depends on: T37.
  - Work: Create DeviceItemViewModel and DevicesViewModel snapshot projection with UI-context updates, English states, labels/roots and per-device errors.
  - Done when: View-model tests cover no devices, pending/ready/unavailable/partial failure, duplicate labels and informative ambiguous-reader classification without cross-thread mutations.

- [ ] **T39. Expose active source and source-volume commands.** FR-7, FR-8, FR-9, FR-10, FR-11, FR-13, FR-18
  - Depends on: T38, T05, T08.
  - Work: Bind independent optical/shared removable preferences, multi-volume choices and constructed source path through Core rules.
  - Done when: Tests retain independent choices, require ambiguous selections, preserve complete Location and display unavailable errors without fallback or game-file probes.

- [ ] **T40. Expose safe-removal and explicit-retry commands.** FR-4, FR-5, FR-14, FR-15, FR-16, FR-17
  - Depends on: T39.
  - Work: Add async row commands and availability properties, disabling conflicting operations only for the affected device.
  - Done when: Tests show native errors and retry, align command predicates with IsEnabled properties and leave unrelated devices/catalog actions interactive.

- [ ] **T41. Project per-game PSP destination choices.** FR-12, FR-13, FR-14, FR-17
  - Depends on: T40, T06.
  - Work: Create MainViewModel.Devices partial integration, optional device view-model injection and selected-game change notifications.
  - Done when: Tests switch between two games with different PSP choices, cover zero/one PSP and preserve existing MainViewModel constructor/catalog behavior.

- [ ] **T42. Render typed device and source-selection controls.** FR-5, FR-9, FR-10, FR-11, FR-17, FR-18, FR-19, NFR-4
  - Depends on: T41.
  - Work: Create DevicesView with typed templates, grouped physical rows/volumes, source selectors, volume selector, state/errors and device-level actions.
  - Done when: Headless controls reflect source and volume choices including missing/duplicate labels; typed bindings compile and no view contains business/native logic.

- [ ] **T43. Integrate the bounded panel and PSP selector into the catalog.** FR-12, FR-17, NFR-3, NFR-4
  - Depends on: T42.
  - Work: Modify MainWindow to display the device panel and selected-game PSP selector without hiding catalog/CSV flows; add accessible names and keyboard focus.
  - Done when: Headless tests select a PSP per game, operate device controls by keyboard and preserve catalog selection/import/export controls at the supported minimum window size.

- [ ] **T44. Start and stop device discovery with application lifetime.** FR-1, FR-3, FR-13, FR-14, FR-16, NFR-3
  - Depends on: T43.
  - Work: Compose in App after UI initialization, start discovery asynchronously and cancel/dispose on shutdown without persisting choices.
  - Done when: Startup/shutdown tests show connected devices are considered on every execution, choices reset, late completion is ignored and native subscriptions are disposed.

- [ ] **T45. Verify source and destination selection as a functional flow.** FR-7, FR-8, FR-9, FR-10, FR-11, FR-12, FR-17, FR-18
  - Depends on: T44.
  - Work: Complete a headless fake-provider flow with two USB/card devices, independent optical source, multiple volumes and two PSPs.
  - Done when: The flow selects sources/volumes and distinct PSPs for two games, preserves preferences across selection changes and never probes/copies a game file.

- [ ] **T46. Verify failures, reconnection and responsiveness as a functional flow.** FR-4, FR-5, FR-6, FR-13, FR-14, FR-15, FR-16, NFR-3
  - Depends on: T45.
  - Work: Hold authorization/removal pending while browsing the catalog, inject denial/busy/disconnect and reconnect the same reliable device at a new root.
  - Done when: Headless input remains responsive, errors are visible, no fallback/prompt loop occurs and recovered selections use fresh roots; restart clears choices and re-enables mounting.

- [ ] **T47. Verify feature boundaries and catalog regressions.** FR-2, FR-6, FR-7, FR-11, NFR-2, NFR-5
  - Depends on: T46.
  - Work: Review dependency direction and use spies to forbid catalog mutation, game-file access, formatting/repair/force and credential collection; run catalog regression checks.
  - Done when: Boundary checks and existing CRUD/import/export tests pass, stored Locations/identities remain unchanged and a fake additional provider preserves association behavior.

- [ ] **T48. Measure build, regression and coverage results.** FR-1–FR-19, NFR-1–NFR-5
  - Depends on: T47.
  - Work: Run root solution build/tests with existing coverage tooling and write coverage.md with owning-suite line/branch results and significant gaps.
  - Done when: Actual commands/results and coverage totals are recorded, build/tests pass, and skipped/native/hardware gaps are explicitly distinguished from verified behavior.

- [ ] **T49. Observe Linux startup, insertion and native authorization.** FR-1, FR-2, FR-3, FR-4, FR-5, FR-18, FR-19, NFR-1, NFR-4
  - Depends on: T48.
  - Work: Use explicitly selected disposable source hardware and available PSPs to execute the Linux discovery/mount/authorization part of manual-validation.md.
  - Done when: Observed startup/hot insertion, existing mount reuse, per-volume roots and Polkit approval/denial are documented with hardware coverage; unavailable mandatory cases keep this task incomplete.

- [ ] **T50. Observe Linux removal, reconnection and session behavior.** FR-6, FR-9, FR-10, FR-11, FR-12, FR-13, FR-14, FR-15, FR-16, FR-17, NFR-1, NFR-3
  - Depends on: T49.
  - Work: Execute Linux safe-remove/busy-volume, disconnect/reconnect, source selection, per-game PSP and restart scenarios without file copying.
  - Done when: Native observations confirm actual errors, whole-device scope, fresh roots, no fallback/remount loop and restart semantics; missing hardware cases remain incomplete.

- [ ] **T51. Observe Windows startup, mounting and UAC authorization.** FR-1, FR-2, FR-3, FR-4, FR-5, FR-18, FR-19, NFR-1, NFR-4, NFR-5
  - Depends on: T48.
  - Work: On Windows, validate discovery/mount roots and allow/deny/cancel elevation with explicitly selected devices; exercise development and published operation-host launch paths.
  - Done when: Real Windows observations and both launch paths are documented, normal UI stays unelevated and UAC failures are reported without prompt loops; absent Windows access keeps this task incomplete.

- [ ] **T52. Observe Windows removal, reconnection and session behavior.** FR-6, FR-9, FR-10, FR-11, FR-12, FR-13, FR-14, FR-15, FR-16, FR-17, NFR-1, NFR-3
  - Depends on: T51.
  - Work: Execute Windows multi-volume removal/veto, optical eject, root-changing reconnect, per-game PSP and restart scenarios with no game copying.
  - Done when: Actual Windows errors/states and source/destination behavior meet the acceptance criteria; no forced removal, stale-root reuse or silent fallback is observed, with missing cases kept incomplete.

- [ ] **T53. Reconcile all-model evidence and final acceptance readiness.** FR-1–FR-19, NFR-1–NFR-5
  - Depends on: T50, T52.
  - Work: Reconcile the PSP model/storage matrix, all ACs, native observations and automated results in manual-validation.md and coverage.md; review constitution compliance and prepare validation handoff.
  - Done when: Every acceptance criterion has current supporting evidence, all-model recognition coverage is substantiated and mandatory platform/hardware gaps are resolved; the user is asked to authorize the separate validation/code-review phase, without declaring the feature implemented.

## Coverage and acceptance checkpoints

| Requirement | Main tasks |
| --- | --- |
| FR-1 | T02, T10, T16–T19, T24–T27, T36–T37, T44, T49, T51 |
| FR-2 | T01, T15, T18, T26, T49, T51, T53 |
| FR-3 | T11, T16–T17, T20, T25, T28, T33, T44, T49, T51 |
| FR-4 | T12, T20, T28, T31–T35, T40, T46, T49, T51 |
| FR-5 | T02, T10–T12, T16–T20, T24–T27, T34, T38, T40, T42 |
| FR-6 | T07–T09, T10, T17, T19, T25, T28, T46, T50, T52 |
| FR-7–FR-8 | T03, T05, T07–T08, T39, T45 |
| FR-9–FR-10 | T03–T04, T39, T42, T45, T50, T52 |
| FR-11 | T04–T05, T07–T08, T39, T45, T50, T52 |
| FR-12 | T06, T41, T43, T45, T50, T52 |
| FR-13 | T04, T06, T09, T14, T18, T26, T41, T44, T46, T50, T52 |
| FR-14 | T09–T10, T13–T14, T18–T19, T21, T26–T27, T29, T46 |
| FR-15–FR-16 | T13, T21–T22, T29–T30, T31–T34, T40, T46, T50, T52 |
| FR-17 | T38–T43, T45, T50, T52 |
| FR-18 | T02, T05, T11, T13, T17, T20–T21, T24–T25, T28–T29, T39, T42 |
| FR-19 | T03, T17–T18, T24, T26, T38, T42 |
| NFR-1 | T16–T36, T48–T53; native evidence requires both platforms |
| NFR-2 | T02, T14, T37, T47 |
| NFR-3 | T10, T12, T14, T27, T40, T43–T44, T46, T50, T52 |
| NFR-4 | T08, T38, T42–T43, T49, T51 |
| NFR-5 | T15, T31–T35, T47, T51 |

- AC-1: T49, T51, backed by the relevant adapter/coordinator tests.
- AC-2: T45, T50, T52; no game-file presence checks.
- AC-3: T46, T50, T52; recovery is within the same session only.
- AC-4: T46, T50, T52; reject busy-volume removal without force.
- AC-5: T42–T46, T50, T52; visible controls, keyboard and responsiveness.
- AC-6: T14, T37, T47; extension through contracts without real deferred providers.
- AC-7: T47, T53; no destructive/copy/credential behavior.
- AC-8: T05, T07–T08, T45, T49–T52; selected volume root plus full stored Location.
- T48 measures coverage; T53 checks completion readiness. Formal SDD validation and code review require separate user authorization after implementation.
