# T10 — Concurrent-copy verification and acceptance traceability

Evidence date: 2026-11-18. Specification 0.6; plan/tasks 0.3.
Status: **incomplete; T10 remains open**.
The user approved the displayed plan and partial continuation with
“Apruebo el plan y la continuación parcial con las pruebas pendientes”.
Work starts from clean local `dev` at `77d5f11` on
`feature/005_T10_concurrent-copy-verification`; no commit or push is performed.
This is task verification, not formal SDD validation/code review or acceptance.

## Evidence boundaries

The available host is Linux x86_64. The current SDK/test platform, commands,
counts, owning-component coverage, package checks and graphical observations are
recorded in [coverage.md](./coverage.md). Earlier consolidation for specification
0.5/plan 0.2 is historical; its one-slot and selection-change expectations do
not describe the approved concurrent production flow.

No Windows execution host or explicitly designated disposable PSP/source media
has been established for this continuation. Windows provisioning, physical
copy/overwrite, real-device discovery/mount/eject and OS logout are not executed.
The approved partial continuation preserves those mandatory gates. See
[pending validation](./pending-validation.md) and
[Spec 004 native prerequisites](../004-device-mounting/pending-validation.md).
Actual Linux temporary-root filesystem and packaged 7-Zip tests are native
software evidence. They are not proof of PSP/FAT, USB/optical loss, Polkit/UAC or
Windows behavior. Controlled inventories/UI I/O are identified separately.

## Requirement and acceptance evidence index

This index links implementation owners to existing behavioral suites, including
T11–T19 concurrency evidence. It is not an implementation-review verdict.
The [technical plan](./plan.md#requirement-and-acceptance-traceability) retains
its approved design mapping. Every physical procedure below is **BLOCKED and
unexecuted on both Linux and Windows**, even if its automated counterpart passes.

| Requirement | Acceptance | Implementation and automated evidence | Pending physical procedures |
| --- | --- | --- | --- |
| FR-1, FR-2 | AC-1 | [Submission drafts](../../src/PSPMan.Desktop/ViewModels/CopySubmissionViewModel.cs), [capture rules](../../src/PSPMan.Core/Transfers/GameCopySubmissionRules.cs); [draft/choice tests](../../tests/PSPMan.Desktop.Tests/CopySubmissionDraftTests.cs), [explicit batches](../../tests/PSPMan.Desktop.Tests/CopySubmissionFlowTests.cs): selection writes nothing, missing/ambiguous PSP, one captured destination per game | P01, P02, P19, P23 |
| FR-3 | AC-2 | [Engine](../../src/PSPMan.Core/Transfers/GameCopyService.cs); [Linux sources](../../tests/PSPMan.Infraestructure.Tests/LinuxCopySourceTests.cs), [submission flow](../../tests/PSPMan.Desktop.Tests/CopySubmissionFlowTests.cs): full Location, local mounted reader/volume and no fallback | P03, P20 |
| FR-4 | AC-3 | [Shared resources](../../src/PSPMan.Infraestructure/Transfers/CopyDestinationResources.cs); [rules](../../tests/PSPMan.Core.Tests/GameCopyConcurrencyRulesTests.cs), [resource integration](../../tests/PSPMan.Core.Tests/GameCopyResourceIntegrationTests.cs), [native ledger](../../tests/PSPMan.Infraestructure.Tests/CopyDestinationResourcesTests.cs), [native engine](../../tests/PSPMan.Infraestructure.Tests/GameCopyEngineResourceTests.cs): actual pending reservations, settlement/retry/skip and retained bytes | P04, P21 |
| FR-5 | AC-4 | [Engine](../../src/PSPMan.Core/Transfers/GameCopyService.cs); [native service](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs), [native overlap](../../tests/PSPMan.Infraestructure.Tests/GameArchiveConcurrencyTests.cs): ZIP/RAR tree, hashes, shared-source sessions and closed handles | P05, P19, P20 |
| FR-6 | AC-5, AC-22 | [Engine](../../src/PSPMan.Core/Transfers/GameCopyService.cs); [service](../../tests/PSPMan.Core.Tests/GameCopyServiceTests.cs), [native engine resources](../../tests/PSPMan.Infraestructure.Tests/GameCopyEngineResourceTests.cs), [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): per-file skip, wait/reinspect, changed target and retired consent | P06, P22 |
| FR-7 | AC-6 | [Rows](../../src/PSPMan.Desktop/ViewModels/CopyOperationViewModel.cs); [concurrent rows](../../tests/PSPMan.Desktop.Tests/ConcurrentCopyRowsTests.cs), [presentation](../../tests/PSPMan.Desktop.Tests/ConcurrentCopyPresentationTests.cs): captured metadata, actual-byte bar, waits and independent commands | P07, P19, P24 |
| FR-8 | AC-4, AC-10, AC-18 | [Engine](../../src/PSPMan.Core/Transfers/GameCopyService.cs); [service](../../tests/PSPMan.Core.Tests/GameCopyServiceTests.cs), [UI flow](../../tests/PSPMan.Desktop.Tests/GameCopyFlowTests.cs): release-before-outcome, partial/all skip and qualified notification | P05, P08, P19 |
| FR-9 | AC-7, AC-18 | [Registry](../../src/PSPMan.Core/Transfers/GameCopySessionCoordinator.cs); [registry tests](../../tests/PSPMan.Core.Tests/GameCopyOperationRegistryTests.cs), [native resources](../../tests/PSPMan.Infraestructure.Tests/GameCopyEngineResourceTests.cs), [rows](../../tests/PSPMan.Desktop.Tests/ConcurrentCopyRowsTests.cs): owning cancellation, wait/release and retained prefix | P09, P22, P24 |
| FR-10 | AC-8 | [Registry](../../src/PSPMan.Core/Transfers/GameCopySessionCoordinator.cs); [registry tests](../../tests/PSPMan.Core.Tests/GameCopyOperationRegistryTests.cs), [native engine](../../tests/PSPMan.Infraestructure.Tests/GameCopyEngineResourceTests.cs): individual fault, all captured dependants on device loss and no replacement redirection | P10, P25 |
| FR-11 | AC-9, AC-18 | [Explicit send](../../src/PSPMan.Desktop/ViewModels/MainViewModel.CopySubmissions.cs), [registry](../../src/PSPMan.Core/Transfers/GameCopySessionCoordinator.cs); [submission flow](../../tests/PSPMan.Desktop.Tests/CopySubmissionFlowTests.cs), [aggregate shutdown](../../tests/PSPMan.Desktop.Tests/ConcurrentCopyShutdownTests.cs): additions while active, per-resource waits and closed admission | P07, P19, P22, P26 |
| FR-12 | AC-10 | [Rules](../../src/PSPMan.Core/Transfers/GameCopyRules.cs); [Linux confinement](../../tests/PSPMan.Infraestructure.Tests/LinuxCopyFileAccessTests.cs), [native service](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs): unsafe/link skip with no outside writes | P08 |
| FR-13 | AC-12 | [Rows/input](../../src/PSPMan.Desktop/ViewModels/CopyOperationViewModel.cs); [reader edges](../../tests/PSPMan.Infraestructure.Tests/GameArchiveReaderEdgeTests.cs), [native service](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs), [concurrent rows](../../tests/PSPMan.Desktop.Tests/ConcurrentCopyRowsTests.cs): password retry/cancel and independent masked input | P11, P24 |
| FR-14 | AC-13 | [Native session](../../src/PSPMan.Infraestructure/Transfers/SevenZipNativeSession.cs); [bridge edges](../../tests/PSPMan.Infraestructure.Tests/NativeArchiveBridgeEdgeTests.cs), [reader edges](../../tests/PSPMan.Infraestructure.Tests/GameArchiveReaderEdgeTests.cs), [native overlap](../../tests/PSPMan.Infraestructure.Tests/GameArchiveConcurrencyTests.cs): ordered parts, missing/corrupt input and independent reads | P12, P20 |
| FR-15 | AC-14 | [Engine](../../src/PSPMan.Core/Transfers/GameCopyService.cs); [bridge edges](../../tests/PSPMan.Infraestructure.Tests/NativeArchiveBridgeEdgeTests.cs), [service edges](../../tests/PSPMan.Core.Tests/GameCopyServiceEdgeTests.cs): empty/early/late fault and retained prior output | P13 |
| FR-16 | AC-15 | [Registry](../../src/PSPMan.Core/Transfers/GameCopySessionCoordinator.cs); [registry tests](../../tests/PSPMan.Core.Tests/GameCopyOperationRegistryTests.cs), [submission flow](../../tests/PSPMan.Desktop.Tests/CopySubmissionFlowTests.cs): immutable choices, removal reservations, dependent drain before forwarding and unrelated continuation | P14, P15, P25 |
| FR-17 | AC-16 | [Application lifetime](../../src/PSPMan.Desktop/ApplicationDeviceLifetime.cs), [aggregate decision](../../src/PSPMan.Desktop/ViewModels/GameCopyViewModel.Shutdown.cs); [shutdown](../../tests/PSPMan.Desktop.Tests/GameCopyShutdownTests.cs), [aggregate races](../../tests/PSPMan.Desktop.Tests/ConcurrentCopyShutdownTests.cs), [real native callbacks](../../tests/PSPMan.Desktop.Tests/NativeConcurrentShutdownTests.cs) | P16, P26 |
| FR-18 | AC-17 | [Native filesystem](../../src/PSPMan.Infraestructure/Transfers/FileGameCopyFileSystem.cs); [Linux access](../../tests/PSPMan.Infraestructure.Tests/LinuxCopyFileAccessTests.cs), [portable Windows rules](../../tests/PSPMan.Infraestructure.Tests/WindowsCopyPathRulesTests.cs), [native resources](../../tests/PSPMan.Infraestructure.Tests/CopyDestinationResourcesTests.cs): duplicates, equality and structural failures; Windows native execution remains pending | P06, P17, P22 |
| FR-19 | AC-19 | [Engine](../../src/PSPMan.Core/Transfers/GameCopyService.cs); [native engine resources](../../tests/PSPMan.Infraestructure.Tests/GameCopyEngineResourceTests.cs), [native service](../../tests/PSPMan.Infraestructure.Tests/GameCopyServiceIntegrationTests.cs), [UI shutdown](../../tests/PSPMan.Desktop.Tests/GameCopyShutdownTests.cs): wrong-password/damage ambiguity, closed output before input, explicit retry/renewed consent, no recopy of completed entries | P18, P24, P26 |
| FR-20 | AC-20 | [Capture rules](../../src/PSPMan.Core/Transfers/GameCopySubmissionRules.cs); [submission flow](../../tests/PSPMan.Desktop.Tests/CopySubmissionFlowTests.cs), [registry](../../tests/PSPMan.Core.Tests/GameCopyOperationRegistryTests.cs): immutable game/source/PSP and same game to another PSP | P14, P19 |
| FR-21 | AC-21 | [Native reader](../../src/PSPMan.Infraestructure/Transfers/SevenZipGameArchiveReader.cs); [native session overlap](../../tests/PSPMan.Infraestructure.Tests/GameArchiveConcurrencyTests.cs), [reader choices](../../tests/PSPMan.Desktop.Tests/CopySubmissionFlowTests.cs): shared archive/parts, independent offsets/passwords and local optical choice | P20 |
| FR-22 | AC-22 | [Shared resources](../../src/PSPMan.Infraestructure/Transfers/CopyDestinationResources.cs); [native resources](../../tests/PSPMan.Infraestructure.Tests/CopyDestinationResourcesTests.cs), [native engine](../../tests/PSPMan.Infraestructure.Tests/GameCopyEngineResourceTests.cs): cancellable same-file waits, current target after holder failure/cancellation and fresh consent | P17, P22 |
| FR-23 | AC-23 | [Explicit send](../../src/PSPMan.Desktop/ViewModels/MainViewModel.CopySubmissions.cs); [submission flow](../../tests/PSPMan.Desktop.Tests/CopySubmissionFlowTests.cs): indexed partial admission, no rollback, no fallback or automatic retry | P23 |
| FR-24 | AC-24 | [Rows](../../src/PSPMan.Desktop/ViewModels/CopyOperationViewModel.cs); [concurrent rows](../../tests/PSPMan.Desktop.Tests/ConcurrentCopyRowsTests.cs), [aggregate shutdown](../../tests/PSPMan.Desktop.Tests/GameCopyShutdownTests.cs): owning input/command identities, stale responses, independent completion and secret retirement | P24, P26 |
| NFR-1 | AC-11 | Native Linux temporary-root suites run; [Windows suites](../../tests/PSPMan.Infraestructure.Tests/WindowsCopyFileAccessTests.cs) and [Spec 004 dependencies](../004-device-mounting/pending-validation.md) remain mandatory/unexecuted | Every P row on both platforms; architecture inventory |
| NFR-2 | AC-6 | [Concurrent UI](../../tests/PSPMan.Desktop.Tests/ConcurrentCopyRowsTests.cs), [submission flow](../../tests/PSPMan.Desktop.Tests/CopySubmissionFlowTests.cs), [shutdown](../../tests/PSPMan.Desktop.Tests/GameCopyShutdownTests.cs): responsive selection, input, additions and cancellation | P07, P09, P19, P24, P26 |
| NFR-3 | AC-11 | Typed [copy view](../../src/PSPMan.Desktop/Views/GameCopyView.axaml), [submission view](../../src/PSPMan.Desktop/Views/CopySubmissionView.axaml), headless binding/layout tests and English artifacts | P02, P03, P07, P17, P24 |
| NFR-4 | AC-2, AC-7 | [Native concurrency](../../tests/PSPMan.Infraestructure.Tests/GameArchiveConcurrencyTests.cs), [native engine](../../tests/PSPMan.Infraestructure.Tests/GameCopyEngineResourceTests.cs), [native closure](../../tests/PSPMan.Desktop.Tests/NativeConcurrentShutdownTests.cs): source hashes, retained output, release; repository write guards in UI fixtures | Before/after source hashes and logical catalogue records for every P row |

## Physical execution prerequisites and run records

Designate the exact disposable PSP(s), their single volumes and source media
before any physical run. Record application revision, OS/build, CPU/RID,
SDK/apphost mode, package/bridge/source hashes, PSP model/firmware,
device/volume identity and generation, mount roots, filesystem/case behavior,
free bytes and existing output. Labels and reused letters alone are insufficient.
Use isolated application data/catalogue and backed-up test volumes. Do not
provision Windows, format media or mount arbitrary devices as part of this
partial continuation. Complete the relevant Spec 004 and copy-native Windows
gates from [pending validation](./pending-validation.md) before dependent runs.
Two-PSP cases require two real PSPs; multiple-disc cases require separate mounted
readers/discs. A VM/controlled inventory is not physical-reader acceptance.

Use licensed fixtures from the [corpus](../../tests/PSPMan.Infraestructure.Tests/Fixtures/Transfers)
or generated harmless payloads, staging complete multipart sets. Maintain an
independent manifest of paths, sizes, empty directories and expected SHA-256
values. Record source-part hashes before/after each run. Never record user
passwords. Compare logical SQLite records consistently; live database/journal
bytes are not a stable catalogue comparison. Selection/draft changes are not
catalogue mutations. Keep output from interruption for inspection; the
application must not clean or restore it. Any external fixture reset occurs only
after identifying the designated volume and recording that retained output.

Every run records: procedure ID, operation IDs, game/source/PSP captures, exact
fixtures/parts/hashes, steps, timestamps and screenshots, expected/actual
notifications and progress, reservation/wait/input order, output hashes or known
partial prefix, skipped/untouched files, source hashes, logical catalogue state,
file/native release before removal/exit, PASS/FAIL/BLOCKED and reason. Record
which operations continued after another failed/stopped. Screenshots alone do
not prove overlapping native I/O, resource release or unchanged source bytes.

## Mandatory physical matrix — all rows BLOCKED on Linux and Windows

P01–P18 retain their identifiers; P07/P14/P16/P18 expectations are reconciled
with specification 0.6. P19–P26 add the approved concurrency conditions. Each row
may require several runs and remains unexecuted until every stated case is
recorded on the designated platform/media.

| ID | Reproducible action | Expected result and observations |
| --- | --- | --- |
| P01 | Start with a designated PSP attached; hot-plug; separately prepare/send with no PSP or unusable volume | Correct identity/mount/root and meaningful missing-destination incident; selection/preparation alone writes nothing |
| P02 | Attach two PSPs; prepare one/many games; explicitly choose the PSP for each submission | One captured destination per operation, chosen root displayed, no ambiguous fallback; unused PSP untouched |
| P03 | Copy from designated DVD/USB/Memory Stick using nested Location; repeat missing/unreadable path | Captured root plus full Location, notification without alternative-source fallback; source/catalogue unchanged |
| P04 | Set SizeBytes above/below real total; use adequate/insufficient/exact reported capacity | Actual decompressed size governs, oversized metadata alone does not reject a fitting copy; low/unknown space fails before writing. Record overhead/later native failures separately; a controlled unknown-space probe is not physical proof |
| P05 | Copy ZIP, RAR4/RAR5 solid and nested Unicode/empty-directory fixtures | Preserved content/tree under `/ISO`, matching hashes, completion only after files/resources close; no format conversion/filter |
| P06 | Pre-create conflicting files; accept one/refuse one; change a target after consent; include duplicate archive paths | Individual consent, refused original unchanged, fresh consent after modification, later duplicate treated as overwrite |
| P07 | Start large harmless copies; add further games while preparing/input/transferring/stopping; operate catalogue and scroll at 900 × 450 | Responsive independent rows/bars/cancel, preparation and resource-wait reasons, further explicit submissions accepted; terminal success/100% only after release. No obsolete one-slot restriction |
| P08 | Mix valid files, unsafe paths, links and refused overwrites; repeat all skipped | No outside writes/link following; valid entries continue; qualified skipped-files completion even when every entry is omitted |
| P09 | Cancel preparation, resource wait, password/overwrite input and mid-write of new/existing files; repeat late/repeated stop | Only owner stops, files/claims close, partial/overwritten output remains with warning; no restoration/deletion; unrelated work continues and terminal result cannot reverse |
| P10 | Disconnect source/PSP separately; replace a captured volume/root; induce write/read-only/full/flush failure on disposable media | Owning failure/reason and retained-output warning, no redirection; all captured device dependants fail while unrelated copies continue; record native error/release ordering |
| P11 | Enter wrong then valid password for encrypted data/header fixtures; separately dismiss/cancel | Masked independent input, explicit retry or cancellation, matching output on success and retained output on stop; no secret in reports |
| P12 | Copy complete split ZIP and multipart RAR from non-first part; repeat missing/damaged later parts | Archive-defined ordering and matching output; unavailable/corrupt parts stop owner with notification and retain earlier/partial output |
| P13 | Copy empty, definitely corrupt first entry and late corrupt entry/part fixtures | No copied content for empty/early failure; late failure stops further work, retains written files, releases resources and reports failure |
| P14 | Change focused catalogue game, selected games and local source/PSP choices during active/waiting/input operations; submit another game afterward | Captured operations remain immutable and continue; only later submission uses its own choices. Captured device replacement/loss is tested separately in P10/P25 |
| P15 | Request ordinary safe removal of active source/PSP, including native veto/authorization delay | New dependent admission blocked; all dependants including waiters stop/release before the same removal proceeds; unrelated work continues; normal native errors visible, no forced removal |
| P16 | Close with preparation/write/password/overwrite/FR-19/resource waits; decline, repeat and accept; race completion/removal/startup and normal OS desktop shutdown | One aggregate decision; decline preserves work/input/drafts and reopens admission; accept drains all before exit, keeps prior/partial/overwritten output and admits no late work. Forced kill is not graceful-shutdown proof |
| P17 | Test case-only paths on actual destination semantics, duplicates, incompatible names and file/directory conflicts | Native equality controls collisions/consent; structural/name failure terminates its owner with retained bytes, without silently renaming or stopping unrelated copies |
| P18 | Use wrong-password and damaged encrypted fixtures producing ambiguous decode; pause, retry with consent, separately cancel; compare definite CRC/ordinary I/O failures | Ambiguous wording, no further writes and closed current output before explicit choice; retry requires current consent and does not recopy completed entries; other operations/input remain independent |
| P19 | Overlap several games on one PSP, then on two PSPs; send the same game to a second PSP while the first runs; add more later | Genuine I/O overlap where resources permit, immutable independent IDs/progress/cancel/outcomes; no global serial slot or predefined numeric limit; source/catalogue preserved |
| P20 | Share one source/archive and multipart set across active copies; use several games from one DVD; then two mounted readers/discs with per-game source choices | Independent read positions, ordered parts/passwords and unchanged hashes; one reader serves its mounted disc, explicit reader choice per submission, no source substitution after media change |
| P21 | Admit operations near shared free-space limits; repeat on separate PSPs; skip entries, cancel after partial writes and retry password work | No double promise of free bytes, actual pending work settles once, retained bytes are not refunded; low-space owner fails rather than waits; retry revalidates incomplete work and other PSP budget is independent |
| P22 | Target the same/native-equivalent file from two operations; hold consent/write, cancel waiter, then fail/cancel holder leaving bytes; modify target after waiter consent | No simultaneous same-file writes, cancellable wait, current inspection and own fresh consent after release; unrelated files advance; actual case/alias and shared-directory semantics recorded |
| P23 | Send ready and missing/ambiguous/unavailable choices together; correct one rejected draft afterward | Ready subset starts independently; indexed incidents remain for others with no rollback, fallback or automatic send; correction requires another explicit submission and later preparation failure belongs only to its owner |
| P24 | Await simultaneous password, overwrite and FR-19 decisions while a third operation runs; answer/cancel one; retry stale/retired controls | Responses affect only their current owner/prompt; unrelated work/input continues; stale, replaced, terminated or accepted-closing decisions cannot authorize writes; secrets cleared |
| P25 | Share source/PSP across active/input/waiting owners and keep another operation on unrelated devices; disconnect or request removal of shared device | Every captured dependant stops/releases, unrelated operation continues, new dependencies blocked during removal; fresh native observation before ordinary forwarding, no fallback or orphan handles |
| P26 | Close while native callbacks/output closure or removal are delayed, with pending drafts; race one/all completions, repeated decisions and late startup; repeat programmatic close and OS logout | Buttons remain reachable at 900 × 450; decline preserves all owners/input/drafts; accept/programmatic shutdown drain before detachment/providers/exit; one cleanup/exit, retained bytes and no late admission. OS logout tested directly, not inferred from a killed process |

## Restart and completion gate

1. Establish designated native hosts, deployment architectures and disposable
   media; record what is actually available instead of assuming Linux x64 is the
   entire supported target set.
2. Complete the outstanding copy T01/T03/T05/T06/T12/T14/T15 Windows gates and
   Spec 004 recognition/mount/authorization/removal prerequisites before their
   dependent physical cases. Use required-native guards and retain reports.
3. Execute P01–P26 on both platforms with the above run records. Keep unavailable
   cases BLOCKED; passing software probes do not satisfy them.
4. Run current build/regression and sequential owning coverage for any resulting
   revision; retain package/source provenance and per-operation hash evidence.
5. Keep T10 unchecked until every mandatory condition passes. Only then request
   separate SDD validation/code-review authorization and later user acceptance.
