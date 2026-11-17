# T10 — Native/manual verification and acceptance traceability

Evidence date: 2026-11-17. Specification 0.5; plan/tasks 0.2.
Status: **incomplete; T10 remains open**.
The user approved the presented T10 plan with “apruebo el plan”, including
consolidation under the continuing Windows dependency deferral. This is task
verification, not the separately authorized SDD validation/code-review phase.
Work started from clean local dev `eb28eb6` on
`feature/005_T10_regression-coverage-native-evidence`.

## Evidence boundaries

The current host is Linux x86_64 with .NET SDK 10.0.404. No Windows execution
host or explicitly designated disposable PSP/source media has been established
for this task. No Windows provisioning, arbitrary-media discovery/mounting or
physical overwrite test was performed. A request for available Windows/test
media was sent to the user; lack of an answer does not authorize using hardware.
[Pending validation](./pending-validation.md) retains T01/T03/T05/T06 and
[Spec 004 native dependencies](../004-device-mounting/pending-validation.md).

[Coverage and current automated results](./coverage.md) distinguish actual
native archive/filesystem tests on temporary directories from synthetic device
inventories and controlled UI I/O. Temporary directories are not a PSP's FAT
volume. [T08 UI evidence](./copy-screen-interactions.md) and
[T09 shutdown evidence](./confirmed-shutdown.md) are historical scoped evidence:
real X11 controls with controlled providers do not prove physical copy/removal.
There is no claim of PSP, Windows, OS logout or all-architecture acceptance.

## Requirement/acceptance evidence index

The linked test suites were included in the current full regression. These are
behavioral evidence links, not a formal implementation-review verdict. “P” rows
below remain unexecuted on **both Linux and Windows**; no row is passed merely
because corresponding automated assertions pass.

| Requirements | Acceptance | Actual automated evidence | Pending physical/native procedure |
| --- | --- | --- | --- |
| FR-1, FR-2 | AC-1 | [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): missing/ambiguous PSP and explicit copy; [coordinator](../../tests/PSPMan.Core.Tests/GameCopySessionCoordinatorTests.cs): captured association and no archive opens for missing input | P01, P02 |
| FR-3 | AC-2 | [Linux source confinement](../../tests/PSPMan.Infraestructure.Tests/LinuxCopySourceTests.cs), [coordinator](../../tests/PSPMan.Core.Tests/GameCopySessionCoordinatorTests.cs): per-medium selected source and no fallback | P03 |
| FR-4 | AC-3 | [service](../../tests/PSPMan.Core.Tests/GameCopyServiceTests.cs): actual-size mismatch, exact/low/unknown space; [edge cases](../../tests/PSPMan.Core.Tests/GameCopyServiceEdgeTests.cs): invalid size and retry capacity | P04 |
| FR-5 | AC-4 | [native service integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): ZIP/RAR, nested hashes and closed handles | P05 |
| FR-6 | AC-5 | [service](../../tests/PSPMan.Core.Tests/GameCopyServiceTests.cs): per-file skip, changed-target consent and duplicates; [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): renewed consent and retired buttons | P06 |
| FR-7 | AC-6 | [rules](../../tests/PSPMan.Core.Tests/GameCopyRulesTests.cs), [native integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): combined byte progress; [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): responsive minimum-size window | P07 |
| FR-8 | AC-4, AC-10, AC-18 | [service](../../tests/PSPMan.Core.Tests/GameCopyServiceTests.cs) and [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): some/all skipped and terminal notification after release | P08 |
| FR-9 | AC-7, AC-18 | [service edge cases](../../tests/PSPMan.Core.Tests/GameCopyServiceEdgeTests.cs): pending I/O/input/release cancellation; [native integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): exact retained prefix, including overwrite | P09 |
| FR-10 | AC-8 | [service](../../tests/PSPMan.Core.Tests/GameCopyServiceTests.cs): retained bytes on failure; [coordinator](../../tests/PSPMan.Core.Tests/GameCopySessionCoordinatorTests.cs): identity/generation/root replacement without redirection; [Linux I/O](../../tests/PSPMan.Infraestructure.Tests/LinuxCopyFileAccessTests.cs) | P10 |
| FR-11 | AC-9, AC-18 | [coordinator](../../tests/PSPMan.Core.Tests/GameCopySessionCoordinatorTests.cs): one active slot; [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): stopping row rejects second start | P07, P09 |
| FR-12 | AC-10 | [rules](../../tests/PSPMan.Core.Tests/GameCopyRulesTests.cs), [Linux file access](../../tests/PSPMan.Infraestructure.Tests/LinuxCopyFileAccessTests.cs), [native integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): unsafe/link skip and no outside writes | P08 |
| FR-13 | AC-12 | [archive reader edge cases](../../tests/PSPMan.Infraestructure.Tests/GameArchiveReaderEdgeTests.cs): typed password/reopen; [native service integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): wrong-password retry; [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): masked input/cancel | P11 |
| FR-14 | AC-13 | [native bridge edge cases](../../tests/PSPMan.Infraestructure.Tests/NativeArchiveBridgeEdgeTests.cs): part callback order; [reader edge cases](../../tests/PSPMan.Infraestructure.Tests/GameArchiveReaderEdgeTests.cs) and [service integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): missing parts and split ZIP/RAR | P12 |
| FR-15 | AC-14 | [native bridge edge cases](../../tests/PSPMan.Infraestructure.Tests/NativeArchiveBridgeEdgeTests.cs): empty/early/late CRC; [service](../../tests/PSPMan.Core.Tests/GameCopyServiceTests.cs): late fault retains output | P13 |
| FR-16 | AC-15 | [coordinator](../../tests/PSPMan.Core.Tests/GameCopySessionCoordinatorTests.cs): selection changes, release-before-removal, veto/repeated requests; [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs) | P14, P15 |
| FR-17 | AC-16 | [shutdown](../../tests/PSPMan.Desktop.Tests/GameCopyShutdownTests.cs): decline/accept, repeated/stale decisions, startup/completion/removal races and retained prior output | P16 |
| FR-18 | AC-17 | [Linux filesystem](../../tests/PSPMan.Infraestructure.Tests/LinuxCopyFileAccessTests.cs), [portable Windows rules](../../tests/PSPMan.Infraestructure.Tests/WindowsCopyPathRulesTests.cs), [native integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): duplicates/structural conflicts; Windows native suite is skipped here | P06, P17 |
| FR-19 | AC-19 | [bridge](../../tests/PSPMan.Infraestructure.Tests/NativeArchiveBridgeEdgeTests.cs) and [native integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): wrong password/damage ambiguity with stopped I/O, closed output and retained hashes; [service retry](../../tests/PSPMan.Core.Tests/GameCopyServiceEdgeTests.cs) and [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): explicit retry/renewed consent, no completed-entry recopy | P18 |
| NFR-1 | AC-11 | Linux native temporary-root tests and portable Windows rules run; Windows native/physical support is **not verified** | P01–P18 on both OSs; Windows prerequisite gates |
| NFR-2 | AC-6 | [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs), [shutdown](../../tests/PSPMan.Desktop.Tests/GameCopyShutdownTests.cs): pending operations keep UI active and cancellation available | P07, P09, P16 |
| NFR-3 | AC-11 | [typed presentation](../../tests/PSPMan.Desktop.Tests/GameCopyPresentationTests.cs), [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs), platform path suites; English evidence documents | P03, P07, P17 |
| NFR-4 | AC-2, AC-7 | [native service integration](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): before/after source hashes for success, cancellation and encrypted failure; [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs) and [shutdown](../../tests/PSPMan.Desktop.Tests/GameCopyShutdownTests.cs): catalogue repository guard; architecture regression | Baseline and after-state comparison for every P row |

## Physical execution prerequisites and record template

Before any physical run, explicitly designate the PSP(s), their single volumes
and disposable source media. Record OS/build, CPU/RID, application revision,
package manifest/source hashes, PSP model/firmware, device/volume identities,
mount roots, filesystem/case behavior and free bytes. Never identify devices by
label or reused drive letter alone. Obtain a Windows host with .NET 10 and the
matching verified runtime; run the Windows prerequisite commands in
[pending validation](./pending-validation.md) first. Multiple-PSP proof needs two
real PSPs. Optical/USB/Memory Stick sources need their designated test media.
Do not create/format media or provision a VM implicitly.

Use only licensed disposable fixtures from the [fixture corpus](../../tests/PSPMan.Infraestructure.Tests/Fixtures/Transfers)
or generated harmless payloads. Stage the *whole* multipart set, with known
password `test` where applicable. Preserve independent expected path/SHA-256
manifests (including empty directories) and hash all source parts before/after.
Record catalogue rows before/after, without changing selection-dependent values
during rows that require a stable selection. For intentional selection tests,
compare persisted records independently of the selected row. The catalogue file
may have SQLite journals; compare logical records consistently, not a live DB's
raw bytes. Never log passwords entered by a user.

For each run record: row ID; exact fixture/part and hash; selected source and PSP;
steps actually performed; timestamps/screenshots; expected versus actual state,
message, destination paths/hashes/partial prefix, original untouched/skipped
files, source hashes and catalogue state; file-release evidence; PASS/FAIL/BLOCKED
and reason. Partial files are expected on interruption; do not delete them as
part of application behavior. Reset disposable test fixtures externally between
runs only after identifying the designated volume and recording retained output.
Every row below is currently **BLOCKED/unexecuted on Linux and Windows**.

| ID | Reproducible action | Expected result and observations to record |
| --- | --- | --- |
| P01 | Start with designated PSP attached; repeat hot-plug; separately select copy with no PSP | Correct mount/identity and root; no-PSP notification; selecting a game alone writes nothing |
| P02 | Attach two PSPs and choose destination through device selection, then copy | Explicit chosen single volume receives output; other PSP untouched; no ambiguous fallback |
| P03 | Copy from each designated DVD/USB/Memory Stick source using nested Location; repeat missing/unreadable path | Captured root plus full Location; meaningful error and no alternative source; catalogue/source unchanged |
| P04 | Set catalogue SizeBytes above and below known real total; provide adequate, insufficient and exact reported free bytes on disposable volumes | Actual total governs; low/unknown capacity gives no writes; exact comparison passes, later overhead/write failure reports retained output. Exercise unknown-space failure through controlled adapter evidence if hardware cannot induce it; do not label that physical proof |
| P05 | Copy ZIP, RAR4/RAR5 solid and nested Unicode/empty-directory fixtures | Complete tree under `/ISO`, known file hashes match; completion only after handle release |
| P06 | Pre-create two conflicting files; accept one and decline one; change next target after consent; include duplicate archive path | Consent per file, declined original intact, changed target requests fresh consent, second duplicate treated as overwrite |
| P07 | Use sufficiently large harmless payload; view catalogue without changing selection during preparation/decode/write/stopping; try second copy | Interactive UI at 900 × 450, one combined actual-byte bar, preparation state before totals, one active row/no queue; final 100% only after release |
| P08 | Mix valid entries, traversal paths, links and refused overwrites; repeat all skipped | No outside write/link following; valid entries continue; “Completed with skipped files” even if all skipped |
| P09 | Cancel preparation, password/overwrite input and mid-write of new/existing output; repeat late stop | Processing stops/files close; retained partial/overwritten bytes and destination warning; no restoration/cleanup; late stop does not reverse terminal result |
| P10 | Disconnect source then PSP in separate runs; reuse mount root with a replacement; separately induce disk-full/read-only/write/flush failure on disposable media | Failure reason and retained-output warning; no writes to replacement; held files/runtime released. Record actual device errors and ordering, not only UI text |
| P11 | Enter incorrect then valid password for encrypted data/header fixtures; separately cancel/dismiss | Retry or cancel, masked input; cancellation retains output; definite error remains distinct from ambiguity |
| P12 | Copy complete split ZIP and multipart RAR, selecting non-first part; repeat missing/damaged later part | Archive-defined ordering and matching hashes on success; missing/damaged part stops with retained output and reason |
| P13 | Copy empty archive, definitely corrupt first entry and late corrupt entry/part | Empty/early failure creates no copied output; late corruption retains prior/partial files and reports failure, never success |
| P14 | Change selected game, selected source and selected PSP during I/O and pending input in separate runs | Current copy fails and warns; no continuation to new selections or stale prompt response |
| P15 | Request safe removal of active source and destination separately, including veto/authorization delay | Copy stops/releases before the same removal request proceeds; ordinary native errors visible, no forced removal; retained files remain |
| P16 | Close during preparation/write/password/overwrite/FR-19; decline, repeat, accept; race completion/removal/startup; separately request normal OS desktop shutdown | Decline keeps application/copy active; acceptance drains current operation before exit; prior completed copies untouched except files deliberately overwritten by current copy; repeated decisions coalesce, no late writes/attachment. Forced kill is not graceful-shutdown proof |
| P17 | Exercise case-only paths on actual destination semantics, duplicate names, incompatible filename and file/directory conflict | Destination-native equality; duplicate consent; incompatible name/structural conflict fails entire copy and retains earlier output |
| P18 | Use wrong password and deliberately damaged encrypted fixture giving ambiguous decode; pause, retry with consent, separately cancel; compare definite CRC and ordinary I/O faults | “Incorrect password or damaged archive”; decoding/writes stop and writable files close before choice; bytes retained; explicit retry only, current partial file needs normal consent, completed entries not recopied; no stale resume or second operation |

## Restart and completion gate

1. Establish designated Windows/Linux hosts, deployment architectures and media.
2. Complete T01/T03 Windows bridge/reader/package and T05/T06 native filesystem/
   service checks, including required-native guards and handle release.
3. Execute every P row on both platforms, preserving per-run records and hashes;
   resolve Spec 004 recognition/mount/authorization/removal dependencies.
4. Rerun build, regression and sequential coverage for the resulting revision.
5. Keep T10 unchecked until all mandatory evidence passes. Only then request
   separate SDD validation/code-review authorization and later user acceptance.
