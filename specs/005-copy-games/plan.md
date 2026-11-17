# Plan 005 — Copy games to a PSP
Status: approved
Revision: 0.2
Specification: [spec.md](./spec.md), approved revision 0.5

## Authorization and scope

The user approved specification 0.4, plan/tasks 0.1 and T01 implementation. T01 exposed backend limitations; the user subsequently authorized reviewing the plan/evaluating another engine with “Autorizo el plan”, then approved specification 0.5 after accepting indeterminate encrypted-error handling. This revision completes that already authorized plan review and proposes a native backend; it is not authorization to implement a replacement. Plan 0.1's approval remains historical; unaffected architecture and product requirements are retained. No T02 work is authorized. See [the evaluation](./archive-backend-evaluation.md) for reference-engine evidence; its pending-behavior discussion is historical and is resolved by approved FR-19/AC-19.

Implement one cancellable copy at a time, full ZIP/RAR contents under the PSP root's `/ISO`, password input/retry, multipart handling, actual-size checks, per-file overwrite consent, skipped-entry outcomes and retained output on cancellation/failure. Coordinate selection changes, safe removal and normal close. Do not add persistence, a queue, concurrent execution, recovery or destination cleanup.

No constitution or AGENTS.md amendments are proposed. Keep Core independent and native/filesystem work in Infraestructure; Desktop owns presentation and application lifetime. Update MEMORY.md at phase boundaries and after each authorized task.

## Existing integration points

Execution update: after native T01's Linux checks passed, the user explicitly
deferred its unavailable Windows checks and authorized the next task, T02.
Proceed with pure Core contracts/rules under that specific dependency exception;
T01 remains open and all Windows/physical acceptance requirements remain mandatory.
See the current [task authorization](./tasks.md). No additional task is authorized.

- `DeviceSessionService` owns observed inventories and associations and already serializes mounting/removal authorization. It exposes `Snapshot`, `SnapshotChanged` and `RemoveDeviceAsync`; removal currently starts without knowledge of copy handles.
- `DeviceAssociationRules` and `MountedGamePathRules` already resolve source groups, source volumes, per-game PSP choices and paths. Reuse these rules rather than detecting a device from a label or directory.
- `DevicesViewModel` currently calls the device session directly for removal and source/destination association changes. Its boundary tests prohibit direct file access. Keep the existing device-only namespace free of archive I/O.
- `MainViewModel` forwards selected-game metadata to Devices and supports catalog-only construction. Preserve those optional consumers and existing import/export/CRUD behavior.
- `ApplicationDeviceLifetime` currently owns Closing and controlled shutdown. It immediately starts disposal after an accepted shutdown request. Copy confirmation must happen before device detachment/cancellation, not in a second competing Closing handler.
- `MainWindow` has compiled bindings and a bounded 900 x 450 minimum layout. Add a typed child copy view rather than business logic in view code-behind.
- Existing Core, Infraestructure and Desktop xUnit projects cover rules, adapters, compiled UI and lifetime. Reuse awaited `HeadlessTestDispatcher` and nonparallel Avalonia fixtures.

## Components and proposed files

Paths are relative to the repository root. Names below are planned, not existing APIs.

| File or group | Responsibility |
| --- | --- |
| `src/PSPMan.Core/Transfers/GameCopyModels.cs` | Immutable request, captured game/device/volume evidence, entry descriptors, progress, prompt IDs, skip reasons and terminal result. Operation IDs distinguish late callbacks from current work. No passwords in published snapshots. |
| `src/PSPMan.Core/Transfers/GameCopyRules.cs` | Pure path classification, checked byte totals, progress accounting, outcome and lifecycle transition rules. Filesystem-sensitive facts arrive from adapters. |
| `src/PSPMan.Core/Transfers/IGameArchiveReader.cs` | Archive-session metadata and ordered extraction contracts. An extraction-target callback prepares each entry and accepts bounded decoded bytes into a caller-owned stream; a source callback supplies selected-volume parts. Expose only .NET types/typed results, never native interfaces, pointers or HRESULTs. |
| `src/PSPMan.Core/Transfers/IGameCopyFileSystem.cs` | Source/volume evidence validation, available space and destination directory/file sessions; explicit create versus conditional overwrite; no deletion/restore contract. |
| `src/PSPMan.Core/Transfers/IGameCopyInteraction.cs` | Awaited password and overwrite requests with operation/prompt IDs, cancellation and retry outcomes. Passwords are transient interaction values, not snapshot/history fields. |
| `src/PSPMan.Core/Transfers/GameCopyService.cs` | One operation's preparation, ordered extraction/copy loop, cancellation, stop reasons, stream ownership and terminal publication. Inject adapters and interaction. No native calls or catalogue repository. |
| `src/PSPMan.Core/Transfers/GameCopySessionCoordinator.cs` | Single active slot, capture/revalidation of session associations and selected game, fail-before-removal sequencing, session observation and disposal. Use one serialized transition gate; never hold it while awaiting UI/native I/O. |
| `src/PSPMan.Infraestructure/Transfers/SevenZipGameArchiveReader.cs`, `SevenZipNativeMethods.cs`, `SevenZipNativeSession.cs` | Native-engine adaptation, source/output/password callbacks, entry indices, error translation, cancellation and native/managed lifetime ownership. |
| `native/PSPMan.ArchiveBridge/` | Proposed small versioned C ABI over official 7-Zip archive interfaces, with native source/build instructions, licensing notices and platform build definitions. It shall not own catalogue/device rules or open PSP paths. |
| `src/PSPMan.Infraestructure/Transfers/ArchiveRuntimeResolver.cs` | Resolve only bundled, pinned native bridge/engine assets by explicit application-local path and runtime architecture. No PATH/tool installation discovery or automatic managed fallback. |
| `src/PSPMan.Infraestructure/Transfers/FileGameCopyFileSystem.cs` | Actual filesystem I/O, free bytes, source-part access, output stream ownership and filesystem error translation. Fixed-size buffers, no whole-game memory or temporary full extraction. |
| `src/PSPMan.Infraestructure/Transfers/LinuxCopyFileAccess.cs`, `WindowsCopyFileAccess.cs` | Platform handle-based root/volume validation, confined traversal, no-follow entry opening and conditional destination mutation. Reuse device evidence; no new mount/eject provider. |
| `src/PSPMan.Infraestructure/PSPMan.Infraestructure.csproj`, `src/PSPMan.Desktop/PSPMan.Desktop.csproj`, native build/publish definitions | After approval, replace SharpCompress with packaged bridge/engine runtime assets and licence/source notices; test publish output. No package/native reference in Core, and no platform loader logic in Desktop view models. Existing T01 characterization remains historical evidence. |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.Copy.cs` | Explicit Copy command, optional copy VM attachment, selected-game change forwarding and opening the copy screen. |
| `src/PSPMan.Desktop/ViewModels/GameCopyViewModel.cs` | UI projection, copy row, current prompt, password retry, overwrite response, cancel and terminal messages. Marshal immutable updates onto UI; reject obsolete operation/prompt responses. |
| `src/PSPMan.Desktop/Views/GameCopyView.axaml` and `.axaml.cs` | Compiled typed table, combined progress bar, input/confirmation states, result and warning text; code-behind initializes only. Use existing controls, not a new DataGrid dependency. |
| `src/PSPMan.Desktop/CopyStartup.cs` | Compose archive/filesystem adapters, coordinator and copy VM using the caller-owned device session. No catalogue writes. |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.cs`, `MainViewModel.Devices.cs` | Preserve existing constructors while routing selected-game changes and attaching copy state independently of catalogue/device startup failures. |
| `src/PSPMan.Desktop/ViewModels/DevicesViewModel.Operations.cs` | Optional awaited removal interceptor: stop/fail matching active copy before forwarding removal; retain normal behavior when absent. |
| `src/PSPMan.Desktop/ApplicationDeviceLifetime.cs`, `App.axaml.cs` | Compose copy with initialized devices; coalesce close requests, confirm active-copy closure before disposal, await copy stop/file release, then existing device shutdown. Preserve provider initialization/disposal race handling and exit error reporting. |
| `src/PSPMan.Desktop/Views/MainWindow.axaml` | Copy action and typed copy screen presentation while catalogue/device selections remain accessible. |
| `tests/PSPMan.Core.Tests/GameCopyRulesTests.cs`, `GameCopyServiceTests.cs`, `GameCopySessionCoordinatorTests.cs` | Pure rules, fake adapters/prompts, interleaving/race tests and no-queue behavior. |
| `tests/PSPMan.Infraestructure.Tests/GameArchiveReaderTests.cs`, native bridge contract tests, `GameCopyFileSystemTests.cs`, `GameCopyIntegrationTests.cs`, platform copy-access tests and `Fixtures/Transfers/` | Real ZIP/RAR/password/multipart fixtures, ABI/callback ownership, filesystem semantics, confinement, space and errors. Keep fixture provenance/hashes and old SharpCompress characterization evidence; revise package placement only through the approved task revision. |
| `tests/PSPMan.Desktop.Tests/GameCopyViewModelTests.cs`, `GameCopyFlowTests.cs`, existing lifetime/selection/device-boundary tests | Awaited UI flows, typed binding/layout, stale prompt rejection, shutdown and removal ordering, catalogue/source non-mutation. Preserve existing architecture guards. |
| `specs/005-copy-games/manual-validation.md`, `coverage.md`, `pending-validation.md` | Later implementation/validation evidence: procedures, actual results and mandatory blocked native tests; these are not claimed as executed by this plan. |

Adjust file granularity only within these responsibilities. There is no schema migration or persistent association change.

## Revised dependency decision and feasibility gate

Propose pinned **official 7-Zip 26.06**, using its full native archive engine (RAR and ZIP handlers) through a small C++ bridge with a C ABI. Load it from Infraestructure; leave Core independent. This is a current need: the managed backend failed required encrypted-multipart input, while the independent native reference decoded the same unchanged sample and its payload hashes. A C++ build dependency and platform runtime assets replace the previously proposed managed dependency; no generic framework/container is added.

The [backend evaluation](./archive-backend-evaluation.md) records downloads/hashes and executed Linux reference checks. Official 7-Zip CLI 26.06 validated 16 representative archive sets; both previously failing families extracted with all three original hashes matching. These are reference-engine observations only. They do not establish that the proposed bridge, managed adapter, callbacks, Windows packaging or cancellation already work.

Use full source/engine handlers, not the reduced LZMA SDK or a reduced executable that lacks RAR. Build and pin the native engine/bridge for the runtime architectures the application publishes on Linux/Windows; verify that none of the application's supported publish targets loses archive support. Source/build configuration, exact inputs, hashes, notices and binary ABI/version checks belong in the package. Keep redistribution notices and required source/build information with delivered native assets; verify them against the release licence and [official developer FAQ](https://www.7-zip.org/faq.html). Do not treat NuGet advisory audit of the managed prototype as a security review of a new native engine.

Reference sources: [official release downloads](https://www.7-zip.org/download.html), [versioned archive interfaces](https://github.com/ip7z/7zip/blob/26.06/CPP/7zip/Archive/IArchive.h), [native client example](https://github.com/ip7z/7zip/blob/26.06/CPP/7zip/UI/Client7z/Client7z.cpp), and release source bundle documented in the evaluation.

The revised T01 gate, after plan/tasks approval, must prove the **actual native API**, not merely rerun the CLI: metadata, index-based duplicate entries, known hashes, passwords and header encryption, multipart/solid decoding, callback-controlled skips/output, actual-size progress, cancellation during decode/input/output, handle release, ABI validation and native/managed error propagation. Include both known-good encrypted multipart/reference-verified samples and the existing failure fixtures. Native Windows and publish-architecture checks must be documented as pending if unavailable, never reported as inferred Linux passes. No native replacement is implemented in this plan-revision phase.

Retain the old package/tests in the working tree until an approved task specifies their replacement or test-only historical retention. Do not mix automatic fallback engines in production. Replacing the package is an implementation action still awaiting authorization.

Rejected alternatives: libarchive's inspected RAR5 handler explicitly rejects encrypted headers; built-in ZIP cannot handle RAR. Calling an installed or bundled 7zz CLI in production would complicate duplicate-entry selection, exact filename framing, password transport and controlled PSP writes. Use it as a reference oracle only, with fixed public fixture passwords, not as the product architecture. Full temporary extraction adds host storage demands and is unnecessary; do not create full temporary game copies during product operation.

### Native bridge boundary

- A versioned C ABI exposes create/open, metadata-by-entry-index, extract selected ordered indices, cancel and close. Use fixed-width fields and explicit lengths/ownership, Unicode conversion and structured native error codes. Do not expose platform-dependent wchar_t/COM layout to C#.
- Only ZIP/RAR handlers are selected. The managed adapter supplies read/seek callbacks for verified read-only source handles and resolves subsequent-part requests through the selected-source filesystem adapter. Native engine code must not open arbitrary archive-supplied host paths.
- Output callbacks request an entry by index. Core applies path/link/filename policy and per-file consent through its extraction target, then supplies a controlled output stream or skip. No native extraction-to-directory method writes directly to the PSP. Index identity preserves duplicate entries without filename-based wildcard selection.
- Execute synchronous native decoding on an off-UI worker. Callback waits for managed password/overwrite input occur without holding the coordinator gate; UI requests use cancellable operation/prompt IDs. Catch managed exceptions inside callbacks, translate to an abort/error and retain the original managed cause. Never unwind exceptions across the ABI.
- Retain delegates, handles and source/output sessions until the native call exits. Cancellation is signalled atomically and checked in progress/read/write callbacks, aborts the engine cooperatively and awaits its return before disposal/removal/close. Do not free state or unload a library while it can call back. Test concurrent progress callbacks; entry/output sequencing and callback thread restrictions come from the pinned interfaces.
- Count decoded callback bytes and successful controlled output bytes for combined progress. Engine progress values may describe compressed or decompressed work depending on phase; do not blindly use them as actual copied bytes. No buffers proportional to the game size.
- Resolve bridge and engine by full trusted application path, with matched ABI/version/runtime evidence. A missing/mismatched native runtime reports a copy-service initialization error; it does not silently select an installed 7z or the old backend.
- Build requirements (C++ compiler, build files and target system libraries) must be explicit. Toolchain availability, actual bridge construction and Windows execution have not been proved by downloading/running the Linux reference executable.

### Approved encrypted-error handling — FR-19

For older encrypted RAR, the native decoder returns data/CRC errors for both wrong passwords and damaged ciphertext. The same ambiguity was reproduced with correct versus incorrect input, and is visible in the handler's operation-result logic. A new engine therefore cannot promise absolute wrong-password/corruption classification.

Approved specification 0.5 now defines this case in FR-19/AC-19. On an encrypted data/header failure whose cause cannot be determined, stop decoding/writes, close current outputs, retain all written content and show “Incorrect password or damaged archive” with explicit password retry or cancellation. Do not publish confirmed corruption, confirmed wrong password, successful completion or an automatic retry. Definitive corruption still fails under FR-10/FR-15; definitive wrong-password results follow FR-13. Native/managed source, destination, selection and I/O failures retain priority as their actual causes and are not converted into password prompts.

The adapter maps native results plus entry/header encryption evidence into a typed IndeterminateEncryptedError result only when the failure belongs to that undecidable class. Complete the native invocation and release its output streams before awaiting the user's new password decision. Retain the coordinator's active-operation slot while awaiting input so another copy cannot start; close/removal/selection-change requests can still stop/fail the operation normally. Associate every prompt with its operation ID and a fresh prompt ID, cancel it on stop/disposal and ignore stale responses.

On explicit retry, revalidate game/source/PSP evidence and reopen the native session with the new transient password. Preserve completed-entry indices and original metadata/evidence; replay preceding solid work to a discard sink without writing completed files again. Reprocess the failed entry only after normal FR-6 destination inspection/overwrite consent, including renewed consent if its retained partial file or another destination changed. Re-evaluate free space for remaining decoded work without treating retained partial content as automatically reclaimable. If the source/archive evidence changed, fail rather than reusing stale entry indices. Keep actual-size/progress accounting consistent without claiming replayed bytes as newly copied. Cancel follows FR-9 and retains output. No automatic retry limit or automatic password guess is introduced.

## Contracts, identity and data flow

1. The explicit Copy command opens the copy screen and captures the selected immutable GameRecord. No PSP means an immediate message, not a disabled action with no explanation. Device-initializing, mounting and selection-required states point to the existing device choices; no unapproved fallback or hidden polling loop starts a copy.
2. Once source and PSP are resolved, capture DeviceKey, VolumeKey, identities, media generations, roots and game ID/metadata. Require exactly one usable PSP volume. Missing/unready evidence is a preparation error. A new explicit action starts again after the user resolves an unavailable source/destination.
3. Reserve the coordinator's single active slot atomically before archive work. The service receives operation-local evidence, cancellation, progress and interaction. It never reads mutable SelectedGame during extraction.
4. Observe device/session changes and selected-game changes. Relevant source/device/volume/root/media changes invalidate the active request; unrelated inventory revisions, labels or provider activity do not fail it merely because a global revision increments. Selection change explicitly fails it under FR-16. Current I/O must stop, not redirect to the new selection.
5. Infraestructure opens source parts read-only and pins their handles/evidence. Inspect archive headers to obtain the actual decompressed total with checked arithmetic, prompting for encrypted headers if needed. This does not require a full payload integrity pass. If metadata is incomplete, a cancellable counting read may be necessary before PSP writes; this fallback uses bounded buffers and no stored full extraction. Unknown/overflowing actual totals fail preparation with a reason, never substitute SizeBytes as authoritative.
6. Compare free bytes on the captured destination volume with that actual total before creating `/ISO` or opening output files. Use the full archive's decompressed byte total as the conservative requirement, without assuming overwrite credit. Capacity may still change after validation; later write failures retain output. Recheck evidence/availability and free-space conditions after lengthy user waits before mutation.
7. Iterate archive entries in their defined order. Classify unsafe paths/links first and omit them. Validate remaining names against actual destination constraints. Inspect each destination via confined handles; directories can merge, but a file/directory conflict fails the operation.
8. For an existing file obtain an evidence token (native identity plus relevant size/change evidence). Await individual consent; inspect again after the response. If it changed, ask again. For an absent file use exclusive creation; if a file appears meanwhile, treat it as a new overwrite requiring consent. Open without truncating first, verify the handle/evidence against the accepted version, and only then truncate/write. Never use a blind overwrite convenience API.
9. Run ordered native extraction through the managed extraction target. Its entry-preparation callback performs steps 7–8 and returns a controlled output stream or skip. Accept bounded decoded chunks, check cancellation/evidence and write with byte accounting; the native engine owns no destination path. Advance skipped entries as required for a solid archive. Close each entry/output before its terminal publication and preserve duplicate indices rather than collapsing paths.
10. On success publish Completed or CompletedWithSkippedFiles. On cancel/failure stop, release files/archive sessions and publish the appropriate reason and destination warning. Never delete PSP output or restore overwritten bytes. All terminal transitions occur after stream release; close errors produce failure. A single atomic terminal gate decides a completion/cancel race; later callbacks cannot reverse the result.

### Safe filesystem access

Lexical prefix checks alone cannot enforce FR-12. Core performs portable classification (absolute paths, traversal, drive/UNC/device syntax, unsafe separators and links); Infraestructure enforces actual filesystem containment at use time. Walk/open entries relative to verified root/directory handles, reject unsafe reparse/symlink traversal, and retain verified parent handles across inspection and mutation. Bind output access to the captured volume identity, not just the mount path. Use Linux handle-relative no-follow operations and Windows handle/reparse-aware opens; validate actual volume evidence before mutations. Use the destination's native name resolution for equality, not `OperatingSystem.IsWindows` as a case-sensitivity shortcut.

Implement tests that swap a parent for a link, replace a target between consent/open/truncation, remount another device at the same path, and use sibling-prefix paths. If portable APIs alone cannot close a race, implement the bounded platform helper here; do not weaken FR-12 or assert filesystem confinement based solely on tests. Source parts must also stay on the chosen source and be opened without following an archive-supplied path outside that source.

### Pure rules and pseudocode

`ClassifyEntry(path, isLink)` returns safe relative segments, SkipUnsafe, or a structural validation result. Platform-invalid names and file/directory facts arrive separately from the filesystem adapter. Unsafe-path skipping takes priority over name rejection for an entry that is itself outside `/ISO`.

`SumActualBytes(entries)` returns a checked nonnegative total or a typed preparation error. Use 64-bit byte counts and decimal/widened ratio arithmetic to avoid multiplying long totals into overflow.

`ReduceOutcome(stopReason, skippedCount, allStreamsClosed)` returns Completed, CompletedWithSkippedFiles, Cancelled or Failed. Refused overwrite and unsafe entries increment skip count; they are never reported as successfully copied.

`AdvanceProgress(total, processedEntries, currentEntryWork)` yields monotonic combined progress. Each file contributes decompression and output processing work to the same bar; stream reads and successful writes update the corresponding counters. For skipped files, resolve their output work without claiming written bytes; decoded bytes required to advance solid archives still count as decompression work. Counting-only preparation remains identified as preparation. With zero eligible bytes use preparation/terminal states rather than divide by zero. Hold displayed progress below 100% until processing and closing have successfully completed; 100% with a skipped-files result means processing completed, not that skipped bytes were copied.

```text
reserve single active operation
try:
    capture and validate chosen game/source/PSP evidence
    open source archive session; obtain password with retry as necessary
    inspect actual total; reject empty archive; check free space before writes
    for entry in archive order:
        check stop reason and relevant device/selection evidence
        if unsafe path or link: record skip; advance reader safely; continue
        inspect destination; fail on incompatible name or file/directory conflict
        while destination requires unconfirmed overwrite:
            await per-file consent
            if declined: record skip; break
            revalidate device and target evidence; repeat if changed
        if skipped: advance reader safely; continue
        open verified destination; decode and write bounded chunks
        close entry/output; update combined progress
    release archive and file sessions
    publish completion, including skipped-files outcome
on indeterminate encrypted decoding failure:
    stop native invocation; close outputs; retain written content
    await explicit password retry or cancellation while retaining active slot
    on retry: revalidate evidence; reopen/replay; apply normal overwrite consent
    on cancellation: publish Cancelled after file release
catch accepted cancellation or other operation failure:
    stop reads/writes; release all sessions; retain output
    publish cancellation/failure, reason and user-handled-output warning
finally:
    reject pending prompt responses; release active slot after stop/close
```

Password retry may need a reopened session at its previous archive position; replay earlier solid entries into a discard stream, never recopy completed entries or repeat their confirmations. The failed/current entry is subject to normal FR-6 overwrite consent. Restart counters for retried work without regressing displayed progress or claiming duplicate written bytes. Indeterminate encrypted errors follow FR-19 as described above; never silently classify them as confirmed wrong passwords. Transport product passwords exclusively through in-memory callbacks, never process arguments/environment/logs. Release transient managed/native credential storage when no longer needed; do not promise erasure of immutable strings or library-internal copies.

## Removal and application lifetime

Use a narrow optional removal interception contract in DevicesViewModel, composed by the application owner. A request for an operation's source or PSP calls coordinator FailAndStopAsync, awaits the same operation completion/file release, then calls existing RemoveDeviceAsync with a fresh observation for the same device identity. Ignore unrelated global-revision changes; reject a replacement generation instead of removing it. The copy fails even if removal subsequently fails. Removal of an unrelated device retains existing behavior. Cover removal racing with copy start using the same coordinator gate and a temporary device-removal reservation; no copy may acquire a device pending removal.

The existing lifetime owner remains the single shutdown entry point. Before setting `_stopping`, cancelling providers or detaching UI, await a copy-close confirmation exposed by the copy VM. Duplicate window/OS close requests share one decision. Decline resets the pending-close request and does not dispose devices or the active copy. Accept requests cancellation, awaits file release, detaches copy subscriptions, then performs existing provider disposal and controlled shutdown. Programmatic disposal still releases owned operations; no destination cleanup occurs. Pending password/overwrite prompts are cancelled on accepted close/stop and stale responses are ignored. Preserve native UAC/Polkit limitations from Spec 004; do not promise immediate dismissal of native prompts.

Copy startup and disposal share the same owner as device startup: a late provider result cannot attach a new copy VM after shutdown, and a copy cannot outlive the session it observes. Retain catalogue-only consumers and independent device/catalogue error messages.

## UI states and accessibility

- Idle/no game: copy screen absent, Copy requires a selected record. A selected game with no usable PSP still allows the explicit action to explain the problem.
- Preparation/error: show the selected game and chosen device/root, or the existing selection action when unresolved; no output writes. A password prompt masks input, permits retry and provides a keyboard-accessible Cancel.
- Transferring: one table row with title, destination, status and combined bar; cancelling changes state to Stopping until handles are released. No fake timer-driven percentage or UI-thread decompression.
- Awaiting overwrite: display the exact destination file and Yes/Skip/Cancel controls plus retained-output warning. A renewed prompt reflects the changed target. There is no Apply to all control.
- Awaiting password decision after an indeterminate encrypted error: display “Incorrect password or damaged archive”, the destination and retained-output warning. Offer masked password retry and Cancel; close current outputs before the prompt, suspend writes, retain the single active slot and preserve access to normal close/device actions. No success/failure cause is fabricated.
- Terminal: Completed, Completed with skipped files, Cancelled or Failed; cancellation/failure show reason/destination and explicitly warn that partial/incorrect output remains for the user. Completed with skips applies even to all-skipped archives. The table is operation/session UI, not persistent history.
- Close confirmation: Accept stops and closes; Decline preserves the active operation. Safe removal stops/fails the copy then proceeds through existing device UI.

Use x:DataType and compiled bindings on view and row templates, AutomationProperties names, keyboard focus order, visible text in addition to color and responsive scrolling/wrapping at 900 x 450. Bind an explicit GameCopyView instance to avoid relying on reflection navigation; if ViewLocator is used, verify the namespace mapping with a headless test. Notifications live in bound UI; no new notification service/framework.

## Test and evidence strategy

Write meaningful behavior tests before each authorized implementation increment and confirm a missing-behavior failure. No build/tests or graphical/native copy runs are performed merely to prepare this plan.

### Automated

- Core rules: source-root retention, unsafe paths, skip outcomes, actual-size mismatch/overflow/zero, combined progress boundaries and duplicate/case semantics supplied by fake filesystem facts.
- Service/coordinator: deterministic pending reads/writes/prompts; single slot; password retry/cancel; empty/corrupt late entries; missing multipart input; per-file refuse/accept/reconfirm; all-skipped completion; byte totals and fake insufficient/unknown space; every failure preserves output; no delete/restore calls exist in the adapter contract.
- FR-19 integration: exercise wrong-password and damaged encrypted input that produce indeterminate results; both must stop I/O, close outputs, retain bytes and show the same non-definitive message with retry/cancel. Test explicit retry after revalidation, failed-entry overwrite consent, completed-entry replay without recopy, no automatic continuation, no second active copy, stale prompt rejection and close/removal/selection invalidation. Confirm ordinary I/O and definite-corruption faults retain their distinct failure paths.
- Identity/races: relevant selection change versus unrelated inventory update, source/destination generation/root loss, replacement device at same path, stop/completion ordering, removal/start reservation, late prompts/progress after disposal. Assert closed handles before removal and before terminal completion.
- Archive integration: small licensed/self-created binary fixtures for plain/encrypted ZIP, ZIP64, split ZIP, RAR4/RAR5, solid RAR, encrypted headers/data, modern/legacy multipart RAR, wrong passwords, bad CRC, truncation and missing parts. Compare decompressed bytes with a fixture manifest. RAR generation is not supported by the proposed library; commit test fixtures with lawful provenance, no proprietary game files and no runtime WinRAR requirement.
- Native bridge integration: prove the same matrix through the proposed ABI on Linux/Windows, with managed source/output callbacks and entry indices. Inject callback errors/cancellation/reentrancy, stale ABI/assets and attempted part paths outside the chosen source. Compare engine-reference results against actual adapter results. Test package publish outputs/architectures and licence/source notices. Characterization of the old backend remains historical and cannot satisfy this gate.
- Filesystem integration: temp roots, nested and empty directories, repeated entries, destination-native case behavior, target replacement during consent, exclusive-create collision, invalid names/file-directory conflicts, symlinks/reparse points and open/truncate races. Native Windows helper tests run on Windows; Linux helper tests run on Linux. Unsupported native environments are explicitly skipped/pending, not simulated passes. Use fault injection for deterministic disk-full/close errors, then demonstrate real-device behavior in manual checks.
- Desktop headless: compiled copy table, responsive pending archive read/write, prompt keyboard states, one bar, actual bytes, cancellation/retention warnings, per-file skips, stale interactions, selected-game failure, safe removal ordering, decline/accept/duplicate close and late startup. Use awaited UI projection, not just Core revision changes.
- Regressions: catalog CRUD/CSV/device-session suites, architecture assembly/direct-IL guards, source/archive hashes and sealed repository spies proving the transfer does not mutate catalogue records. Extend guards for Transfers without excluding existing Devices coverage.

### Commands during authorized implementation/validation

From the repository root: `dotnet build PSPMan.slnx`, then `dotnet test PSPMan.slnx`. Focused tests may run first; final regression uses the full solution. The existing projects use xUnit/VSTest with coverlet.collector; gather coverage sequentially with `dotnet test PSPMan.slnx --collect:"XPlat Code Coverage"` and report owning Core/Infraestructure/Desktop line/branch results and justified gaps. Read the run-tests skill before executing tests. Do not invent a numeric acceptance threshold. Do not instrument while another test/app holds the same binaries.

When a controlled graphical environment exists, run `dotnet run --project src/PSPMan.Desktop/PSPMan.Desktop.csproj` with dedicated test media. Ordinary startup automatically mounts supported devices, so no smoke launch against arbitrary attached hardware.

### Mandatory native/manual matrix

On Linux and Windows with a real USB PSP and dedicated source media, demonstrate: already-connected and hot-plug mounting, no PSP message, multiple PSP choice when hardware is available, source path missing, actual-size capacity mismatch, ZIP/RAR passwords/retry, multipart success/missing part, nested output, overwrite per-file skip/reconfirm, progress responsiveness, cancellation retaining partial output, corrupt/write/disconnect failure, selection-change failure, requested removal after handle release, close-confirm decline/accept and absence of catalogue/source modifications. Compare known fixture hashes after successful copying. Use disposable fixtures to exercise destructive overwrites; all tests operate only on explicitly designated media.

Record platform/device/source filesystem, prerequisites, exact steps, expected/actual result and evidence for each case. Mandatory cases without hardware/OS/media access go into pending-validation.md with their blocker and a future environment/procedure. Keep the Spec 004 pending hardware/authorization/all-model record separate and cross-reference dependencies. No unavailable case counts as passed and no fully verified verdict is possible until required physical tests pass. No provisioning or destructive native test is authorized by this plan alone.

## Requirement traceability

| Requirement | Planned work | Verification / acceptance |
| --- | --- | --- |
| FR-1 | MainViewModel.Copy and typed screen | Explicit-action/no-selection-write UI flow; AC-1 |
| FR-2 | Session capture, existing PSP selection, composition | No PSP/unready/multiple PSP flows and native mounts; AC-1 |
| FR-3 | Existing path rules, source/archive adapter | Mounted-path and missing/unreadable integration; AC-2 |
| FR-4 | Actual total inspection and volume free bytes | Mismatch, unknown/low/exact space, no preflight writes; AC-3 |
| FR-5 | Ordered entry copy, confined filesystem adapter | ZIP/RAR full nested structure/hash tests; AC-4 |
| FR-6 | Interaction and conditional overwrite evidence | Each file, refusal, duplicate and changed-target prompts; AC-5 |
| FR-7 | Byte accounting and typed combined bar | Pending preparation, actual size, monotonic byte progress; AC-6 |
| FR-8 | Outcome reducer and terminal notification | Complete, some skipped, all skipped, closed streams; AC-4/10/18 |
| FR-9 | Stop gate and handle release, retained-output warning | Cancel preparation/input/transfer, no deletion/restore; AC-7/18 |
| FR-10 | Typed errors, identity-bound I/O and retention | Source/destination/error/close faults and output bytes retained; AC-8 |
| FR-11 | Coordinator active slot and removal reservation | Concurrent start rejection and stop/disposal races; AC-9/18 |
| FR-12 | Pure path policy plus native confined handles | Unsafe/link skip and race containment; AC-10 |
| FR-13 | Password interaction and archive reopen/retry | Encrypted headers/data, wrong password, retry/cancel; AC-12 |
| FR-14 | Format-specific multipart discovery/readers | Both ZIP/RAR split families, correct order/missing part; AC-13 |
| FR-15 | Empty/CRC/format error translation | Empty no output; early/late corruption retains prior writes; AC-14 |
| FR-16 | Selection invalidation, awaited removal interception | Relevant changes fail; stop/release precedes safe removal; AC-15 |
| FR-17 | Existing lifetime owner plus close decision | Decline/accept/duplicate close/startup races, no PSP cleanup; AC-16 |
| FR-18 | Ordered duplicates and destination-native name lookup | Duplicate overwrite, case semantics, structural/name failures; AC-17 |
| FR-19 | Typed indeterminate encrypted errors, stopped native invocation, input state and explicit replay/retry | Wrong-password/damaged-input ambiguity, retained output, no automatic writes, per-file consent, retry/cancel/lifetime races; AC-19 |
| NFR-1 | Shared orchestration and two native adapters | Linux/Windows automated and mandatory physical matrix; AC-11 |
| NFR-2 | Off-UI worker, cancellable streams and UI projection | Pending-I/O responsive UI, cancellation and catalogue viewing; AC-6 |
| NFR-3 | English UI and existing path syntax rules | Typed view/text review and platform-root tests; AC-11 |
| NFR-4 | Read-only sources; no catalogue repository in transfer | Before/after hashes, repository spies and boundary tests; AC-2/7 |

Completion is demonstrated by current traceability, automated results, measured coverage, physical matrix, scoped SDD review and final user acceptance. Plan approval does not replace any of those.

## Future concurrency and decisions

Each operation owns its streams, token, progress, prompt identifiers and immutable evidence; the coordinator alone enforces one active operation. This makes future independent operations possible without shared mutable archive readers or a dedicated OS thread per copy. Do not implement that scheduler or remove the single-slot guard now. Streaming work uses asynchronous I/O and an off-UI worker for CPU/synchronous codec work; a thread per game is not a product requirement.

Rejected architecture: putting transfers inside DevicesViewModel/DeviceSessionService would blur device observation with game-file work and violate existing boundary expectations. Keep a separate Transfers coordinator observing the same caller-owned session. No generic event bus, background service framework, dependency-injection container or task scheduler is needed.

## Risks, approval and next step

Execution update: T02 is complete; the user's subsequent “Continúa con la
siguiente tarea” authorized T03 with the previously explicit Windows deferral.
The [production adapter report](./archive-adapter.md) records Linux results and
the still-open Windows gate. The source contract captures an opaque validation
token distinct from the combined inspection fingerprint; cancellation is a
typed callback outcome. These implementation refinements do not alter the
approved product behavior or authorize T04.

The subsequent “Continúa con T04” authorizes only T04, which depends on the
completed T02. Its Linux filesystem implementation and current verification are
recorded in [T04 evidence](./linux-copy-filesystem.md). Directory creation returns
an optional unsafe/link skip reason discovered at mutation time. This preserves
FR-12's existing outcome through the Core boundary; it adds no user behavior.
T01/T03's deferred Windows gates remain open; T05 is not authorized by this step.

The subsequent “Continúa con T05” authorizes only T05. Its Windows filesystem
implementation, portable verification and exact unexecuted native procedures are
recorded in [T05 evidence](./windows-copy-filesystem.md). Canonical observed volume
GUIDs authorize root access, including validated folder mount points; relative
no-follow opens confine entries below that root. No provider or Core contract
change is proposed. The mandatory Windows completion gate remains open and this
authorization does not start T06 or waive native/physical acceptance.

The user subsequently requested a scoped plan before each task. The presented
T06 plan explicitly proposed proceeding with T03/T05 Windows checks deferred;
“Apruebo el plan” approved that scope and dependency exception. T06 implements
only the Core copy engine and its tests, as recorded in [T06 evidence](./copy-engine.md).
Windows integration and all earlier mandatory gates remain open. This approval
does not start T07; present its own scoped plan and await approval first.

The user then requested T07 and approved its presented task-specific plan,
including proceeding with T06 and earlier Windows dependencies still open.
The coordinator and optional awaited removal interception are recorded in
[T07 evidence](./copy-session-coordination.md). This implements the already
planned active slot, relevant-evidence invalidation and pre-removal reservation;
no provider policy or UI composition change is proposed. T08 still requires its
own scoped plan and explicit approval before implementation.

The user subsequently requested the next task and approved the presented T08
plan with “Apruebo el plan”. Its typed copy view, transient interaction bridge,
optional main-model attachment, adapter/coordinator composition and minimal
lifetime draining are documented in [T08 evidence](./copy-screen-interactions.md).
Integration moves only the coordinator's engine runner onto Task.Run, preserving
synchronous selection capture/reservation at action acceptance; queuing the entire
start allowed a later UI selection to substitute for the accepted game. Existing
device policy/contracts remain unchanged. T09's confirmed shutdown is separate
and requires its own scoped plan/approval. All native Windows and physical gates
remain mandatory.

- Native bridge support, runtime packaging, ambiguous encrypted errors, actual metadata completeness and confined handles must be proved early. CLI success is reference-engine evidence, not an implemented adapter. Compatibility gaps stop affected implementation for an approved correction.
- Actual byte totals come from archive metadata or a bounded count pass, not SizeBytes. Source mutation/corruption between inspection and copy remains a failure with retained output, not a guaranteed prevalidated transfer.
- Storage allocation/limits and external actors can invalidate a free-space check or target token; conditional open/identity checks narrow races, and failures remain visible. Do not promise atomic whole-game replacement or power-loss durability.
- Native Windows execution and real PSP/media access were unavailable in the latest recorded project evidence. Reassess availability at implementation time; preserve blocked cases honestly.
- This plan leaves all source code, dependencies, tests, devices and pre-existing local deletions untouched. Implementation branches must later start at local dev with `feature/005_TNN_<description>` after a status check, preserving existing changes. No commit/push is authorized.

## Downstream impact and approval state

| Artifact | Impact | Approval/evidence retained or required |
| --- | --- | --- |
| Spec 0.5 | FR-19/AC-19 resolve the encrypted-error ambiguity and qualify FR-10/FR-15 while preserving other copy/device/output rules. | Revision 0.5 is explicitly approved; no further requirement change is proposed by this plan. |
| Plan 0.1 → 0.2 | Replace managed archive adapter with the native engine/bridge, callback-based extraction contracts and runtime packaging; implement approved FR-19 handling. | Revision 0.1 approval is historical. The user explicitly approved revision 0.2 with “apruebo el plan”. Unaffected architecture remains unchanged. |
| Tasks 0.1 | Revise T01's native gate, T02 contracts/rules, T03 adapter/runtime build work, T06 callback/retry integration, T08 password-decision UI, T09 lifetime races and T10 native/package/FR-19 evidence. T04/T05 filesystem responsibilities remain, with runtime interoperability checks where needed. | The list is not rewritten in this phase. Previously approved unaffected scope is retained; changed dependent scope requires an approved task revision. No T02 implementation is authorized. |
| Old T01 tests/package | Keep reproduced SharpCompress limitations, fixture hashes, build/regression and audit as historical evidence. Remove or retain the old package as test-only only through a subsequently authorized implementation task. | 42 characterization tests and 1475 regression passes do not approve a native adapter. No dependency/test code was changed during this revision. |
| Validation | Add actual callback/ABI, runtime publish and licence/source checks on Linux/Windows while keeping mandatory real-PSP tests. | No Windows/native bridge/PSP result is inferred from Linux CLI checks. |

Review result: specification 0.5's FR-1–FR-19 and NFR-1–NFR-4 are mapped to implementation and verification; no blocking product decision or constitution conflict remains. Native ABI/toolchain, publish-architecture and Windows/hardware checks are implementation/validation gates, not claimed completed work. No AGENTS.md/constitution change is proposed.

Plan revision 0.2 is explicitly approved by the user's “apruebo el plan”. Next: revise and seek approval of affected tasks and authorization to resume T01 under the native-backend scope. Plan approval alone does not authorize implementing the bridge. If coherent task decomposition needs more than about ten increments, propose a split before issuing an oversized list.
