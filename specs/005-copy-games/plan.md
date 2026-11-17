# Plan 005 — Concurrent game copying to PSPs

Status: approved
Revision: 0.3
Specification: [spec.md](./spec.md), approved revision 0.6

## Authorization, baseline and scope

The user approved specification 0.6 with “Apruebo la revisión”, then authorized
preparing this technical plan with “Autorizo preparar el plan técnico”. This
revision replaces the single-operation planning of approved plan 0.2, preserving
unaffected extraction, confinement, identity, retention and packaging decisions.
Plan 0.2 and tasks 0.2 remain historical single-copy scope. The user explicitly
approved this displayed plan with “apruebo el plan y autorizo preparar la revisión
de tareas”. No technical scope changed after presentation. This approves plan
0.3 and authorizes drafting [tasks 0.3](./tasks.md), not implementation.
Existing local specification/memory edits are preserved. During plan preparation
only this plan and MEMORY.md were edited; no build/tests or graphical runs occurred.

Implement session-only submissions of one/many games, independent concurrent
operations on one/many PSPs, captured choices, shared read-only sources, per-file
conflict waits, coordinated actual-size capacity, operation-specific input and
cancellation, device-dependent stop/removal and confirmed all-operation closure.
Do not add persistent queues/history, recovery, global numeric concurrency
limits, automatic failed-copy retries, destination cleanup or format conversion.
One operation still extracts one game's complete ZIP/RAR tree under `/ISO`.

The [constitution](../../docs/constitution.md) and [AGENTS.md](../../AGENTS.md)
apply unchanged: Core policy without platform/UI dependencies, Infraestructure
native/filesystem adapters, Desktop typed presentation/composition, existing
.NET/Avalonia/CommunityToolkit/xUnit stack. No new framework/package, database
schema, constitution or AGENTS amendment is proposed.

## Confirmed implementation baseline

- [GameCopyService](../../src/PSPMan.Core/Transfers/GameCopyService.cs) already
  creates per-call Operation state, streams, terminal arbitration and retry data.
  Reuse the ordered callback pipeline; add shared resource cooperation rather
  than a second extraction engine. Its current free-space check is independent
  per operation and is not safe admission accounting for concurrent copies.
- [GameCopySessionCoordinator](../../src/PSPMan.Core/Transfers/GameCopySessionCoordinator.cs)
  has one active slot/snapshot, rejects a second start and invalidates the active
  game when selection changes. Replace those policies; preserve synchronous
  capture before background execution, identity checks and removal reservations.
- [copy contracts](../../src/PSPMan.Core/Transfers/IGameCopyFileSystem.cs) expose
  held-volume capacity and conditional evidence-checked create/overwrite, but no
  cross-operation destination claims. Path strings alone cannot authorize access
  or establish native case/alias equality.
- [SevenZipNativeSession](../../src/PSPMan.Infraestructure/Transfers/SevenZipNativeSession.cs)
  owns a session semaphore, callbacks and source streams. Keep serialization
  within an archive session, not across all sessions; prove independent sessions
  actually overlap through the packaged native engine before relying on it.
- [GameCopyViewModel](../../src/PSPMan.Desktop/ViewModels/GameCopyViewModel.cs),
  [its shutdown partial](../../src/PSPMan.Desktop/ViewModels/GameCopyViewModel.Shutdown.cs)
  and [GameCopyView](../../src/PSPMan.Desktop/Views/GameCopyView.axaml) have one
  operation/input. [MainViewModel](../../src/PSPMan.Desktop/ViewModels/MainViewModel.cs)
  forwards the focused catalogue row into the coordinator; detach that coupling.
- Device source choices currently use a shared SourceGroup association and PSP
  choices are per game. Submission-local optical choices must not be implemented
  by temporarily changing the shared session association between submissions.
  Existing device providers/removal authorization remain the authority.

This is a scoped planning inspection, not formal SDD review or a claim that
existing code implements revision 0.6. Historical results are linked below.

## Proposed files and responsibilities

Paths below are relative to the repository root; new names are proposed.

| File or group | Responsibility |
| --- | --- |
| `src/PSPMan.Core/Transfers/GameCopyModels.cs` | Add waiting state/reason, immutable submission/admission results, operation sequence and captured source/destination display metadata; retain stable operation/prompt IDs and private passwords. |
| `src/PSPMan.Core/Transfers/GameCopySubmissionRules.cs` (new), existing `Devices/DeviceAssociationRules.cs` if needed | Pure per-game choice resolution against one observed inventory and explicit submission choices; return captured requests or per-game incidents without I/O or association mutation. |
| `src/PSPMan.Core/Transfers/IGameCopyResources.cs` (new) | Platform-neutral asynchronous budget/entry-lease contracts, operation-scoped progress/debit/settlement and cancellation; no native handles or delete/restore operation. |
| `src/PSPMan.Core/Transfers/GameCopySpaceReservations.cs` (new) | Checked per-volume reservation ledger and pure admission/update rules, including retry and terminal unused-claim release. |
| `src/PSPMan.Core/Transfers/GameCopySessionCoordinator.cs` | Multi-operation registry, batch partial admission, immutable requests, per-ID cancellation/snapshots, device dependency index, close admission gate and awaited drain. |
| `src/PSPMan.Core/Transfers/GameCopyService.cs`, `GameCopyRules.cs` | Integrate actual-size reservation, cancellable entry acquisition before inspection, scoped waits, post-write accounting and release; preserve extraction order/retry/retention. |
| `src/PSPMan.Core/Transfers/IGameCopyFileSystem.cs` | Add only the opaque volume/target coordination evidence needed by resource contracts; preserve conditional open and native confinement as final authority. |
| `src/PSPMan.Infraestructure/Transfers/CopyDestinationResources.cs` (new), `FileGameCopyFileSystem.cs`, `CopyAccessStream.cs` | Shared destination-session resource owner, native target claims, namespace mutation coordination and allocation-aware stream settlement; validated roots remain per operation. |
| `src/PSPMan.Infraestructure/Transfers/LinuxCopyDestination.cs`, `LinuxCopyFileAccess.cs`, `LinuxCopyNative.cs`; corresponding Windows files | Native alias/identity resolution for claims, compatible shared directories, no-follow opens, concurrent separate-file streams and native capacity evidence. Adjust source sharing only where tests require it. |
| `src/PSPMan.Infraestructure/Transfers/SevenZipNativeSession.cs`, resolver/methods and `native/PSPMan.ArchiveBridge/` only if necessary | Prove and, within an authorized task, minimally correct independent-session ownership/parallelism; no speculative engine/version/ABI change. Any material ABI/package scope change needs plan/task reassessment. |
| `src/PSPMan.Desktop/ViewModels/GameCopyViewModel.cs`, shutdown partial | Collection owner, admission incidents, per-operation interaction routing, aggregate nonterminal state and one close decision; no single global pending password/overwrite. |
| `src/PSPMan.Desktop/ViewModels/CopyOperationViewModel.cs`, `CopySubmissionViewModel.cs` (new); existing input/close models | Typed row state/commands/input and not-yet-submitted per-game source/PSP choices; immutable rows after admission, transient identity-bound decisions. |
| `src/PSPMan.Desktop/ViewModels/MainViewModel.Copy.cs`, `MainViewModel.cs`, existing DevicesViewModel partials only as needed | Multiple catalogue selection, focused-row CRUD compatibility, explicit prepared-game submission and current native choice projections; no file I/O in device/view models. |
| `src/PSPMan.Desktop/Views/MainWindow.axaml`, `GameCopyView.axaml`, `CopyStartup.cs`, `ApplicationDeviceLifetime.cs` | Typed selection/submission/list templates and composition of one shared resource owner; aggregate confirmation, stop/drain then existing device shutdown. View code-behind remains initialization-only. |
| Existing test projects, focused new concurrency/resource test classes | Deterministic rules/coordinator races, real native archive/filesystem overlap and awaited typed UI/lifetime behavior. Preserve CRUD/CSV/device regressions. |
| Existing feature coverage/manual/pending reports and a concurrency evidence report, during authorized implementation | Distinguish new concurrency results from historical single-copy evidence and record every unavailable native/physical case. |

Names/signatures may be refined within approved responsibilities. No generic
scheduler, DI framework, event bus, notification service or persistence is needed.

## Components, data flow and ownership

`Catalogue selection -> submission drafts -> pure capture/admission -> operation
registry -> independent GameCopyService calls -> shared volume budget/target
claims -> existing native archive/filesystem adapters -> sequenced row snapshots`.

Desktop holds `SelectedGames` independently of the focused `SelectedGame` used
by existing details/CRUD. Use Avalonia multiple selection through bound data or
an initialization-only adapter if required; view models receive GameRecord
values, not controls/event arguments. Existing edit/delete commands keep their
single-focused-record behavior and are never expanded into batch mutations.
Refreshing the catalogue reconciles draft selections by ID; captured requests
retain their immutable record values regardless of later UI/catalogue changes.

Each CopySubmissionViewModel has explicit source/volume and one PSP choice,
seeded from valid existing Spec 004 preferences. Optical choices are local to
that submission. Reuse pure association rules against the latest inventory with
an immutable local choice overlay; do not rewrite shared session preferences to
construct requests. Ambiguous choices remain incidents, never inferred from a
path/label. A submitted batch captures one consistent observed inventory; each
ready item gets a fresh operation ID and its own cancellation/completion owner.
Missing choices produce individual incidents and require explicit resubmission
once resolved. Later capacity/archive errors belong to accepted rows.

The coordinator owns a dictionary keyed by operation ID and publishes immutable
collection snapshots with an increasing revision per row. Synchronous admission
captures requests and records them before scheduling engine work; no UI selection
is reread by the runner. UI commands submit and return admission results promptly,
not await the lifetime of every transfer under the default single-running-command
guard. Removing the old IsBusy gate alone is insufficient. Reject submissions
only for actual admission/identity/removal/close conditions, not another copy.

```text
Submit(drafts, observedInventory):
  under coordinator gate:
    reject if closing/disposed
    for each draft:
      resolve captured request or record incident
      reject that item if a captured device is reserved for removal
      add accepted operation with immutable choices and completion owner
  outside gate: schedule each accepted runner; publish results outside locks

Cancel(operationId): mark only that owner; signal and await its completion
OnInventoryChanged(snapshot): fail/cancel every affected captured owner
  using device/volume identity, generation and availability; ignore UI choices
```

Never hold the registry lock while calling observers, cancelling tokens,
awaiting I/O or native authorization. Reserve shared initialization/disposal and
completion tasks before callbacks to preserve T09's reentrancy guarantees.
A registry entry remains nonterminal until native return and all owned files,
leases and reservations have settled; queued late UI snapshots cannot revive it.

## Per-volume capacity and output accounting

Budget keys use captured device/volume identity plus media generation, not mount
paths or drive letters. Different PSPs have independent ledgers. Inspect actual
size before any destination mutation; preserve checked arithmetic, invalid/
unknown totals and zero-byte-file handling. Unknown/insufficient unreserved
space is a per-operation failure, not a wait-for-space queue. Existing byte
checks give no speculative credit for overwrites, compression, future deletion
or likely skips; decline/unsafe skip releases that entry's pending claim.

Pure rule inputs: known available bytes, outstanding pending bytes by operation,
requested remaining output and current identity/generation. Return a reservation
or the existing typed capacity/overflow error. For an initial stable sample:

```text
TryReserve(operation, volume, actualPending, sampledFree):
  check volume evidence and nonnegative/known checked totals
  otherPending = checked sum of the volume's other outstanding claims
  if actualPending > sampledFree - otherPending: fail this operation
  replace this operation's remaining claim atomically (do not add it twice)
```

The sample and replacement must form one consistent admission transaction with
write accounting. A simple query-free-then-update-progress race is not sufficient:
a concurrent write can already reduce OS free bytes while its reservation is
still pending, or be removed from pending before its allocation becomes visible.
Implement a per-volume accounting boundary with in-flight write epochs and a
brief admission fence: stop new output chunks from entering settlement, await
already-started chunks, reconcile held-volume free bytes plus committed/unsettled
charges, then reserve and resume chunks. Do not hold this fence across source
reads, native decoding, UI input, file-conflict waits or whole-file transfers.
Unsettled writes keep a conservative charge until represented in a consistent
native free-space sample; never count the same free bytes twice. External actors
and filesystem overhead can still cause ordinary reported write failure.

For every successful output chunk, update actual attempted/written settlement
for the current extraction attempt; progress's monotonic maximum is not capacity
accounting. On retry a failed entry may be truncated and written again: reserve
its full remaining replay output before reopening, preserving the retained
file's real consumption and excluding completed entries. Release skipped bytes,
unused reservations and per-operation accounting only after the appropriate
file/attempt release; never report retained output as reclaimed. Final-release
failure remains failure. No rollback/deletion or space-wait admission is added.

Test the accounting protocol with controlled free-space samples before/after
allocation, buffered/partial/failed writes, sampling/write races, retries and
checked overflow; fake progress totals cannot substitute for native settlement.
The implementation must demonstrate no overcommit and no transient double-count
rejection from its own sample/update ordering before the resource layer is done.

## Destination file conflicts and confinement

Share CopyDestinationResources across all copies in a device session. Opaque
native target keys and leases must represent volume identity, canonical parent
identity and native leaf lookup; an OS-wide OrdinalIgnoreCase comparer, raw path
string or per-operation lock cannot establish equality. Cover case-sensitive
Windows directories, FAT case/short aliases and Linux mounted filesystem behavior.
Existing hard links/unsafe paths remain skips under the single-copy rules.

Acquire a target claim before hashing/inspecting an existing file for consent.
A conflicting caller publishes WaitingForResource with a destination-file reason,
awaits cancellably, then revalidates volume/parents and inspects the *current*
file. It must not present stale consent from before waiting. Keep the entry lease
through its overwrite decision, conditional open, writes, flush and file release;
release it on skip, terminal fault or cancelled input. Holding one file for input
does not block different files or another PSP. Never hold two output-file leases
for one archive operation; archive order remains unchanged, avoiding cross-file
hold-and-wait deadlocks. FR-19 closes/releases the failed output before asking
for retry; retry reacquires and reconfirms under current evidence.

Use short native namespace guards for parent traversal/creation and missing-leaf
resolve/create/register transitions. Existing targets use native file identity;
missing aliases cannot both bypass claims: resolve and conditionally create
under the parent namespace guard, register the resulting identity before exposing
its stream, and re-resolve contenders after the first creation. Release namespace
guards before long hashing, user input or data transfer. Directory entries merge
only compatible directories; structural conflicts fail the encountering operation
without deleting/replacing the other operation's parent tree. Traverse guard
hierarchies in a consistent root-to-leaf order. Per-file leases, native conditional
opens and retained parent pins all remain necessary; none substitutes for another.

Keep Linux openat2/statx and Windows relative no-follow handles, conditional
truncation and current-path/evidence checks as final authority. Native sharing
barriers must permit independent reads and different-file outputs while blocking
replacement of pinned objects; do not weaken confinement just to make concurrency
pass. Real platform integration must verify this protocol, including creation
races, aliases, cancellation while waiting and release after close errors.

## Archive/source parallelism and operation-specific input

Each engine call owns its archive session, source streams, password, callback
registrations and cancellation token. Independent streams are required even
when the same archive/part is selected, avoiding shared stream positions.
A reader may inspect/extract serially *within* its own session, but no singleton
runtime/extraction lock may serialize all operations. Use asynchronous file I/O
and off-UI native/CPU work, not a dedicated OS thread requirement per game.

Before broad UI integration, run actual packaged bridge/reader tests with two
independent sessions overlapping in callbacks, including the same source archive
and different passwords/parts. Cancel/fail one and prove the other continues;
await native return before releasing its delegates, stream handles or runtime.
T01's earlier concurrent-progress harness is not proof of concurrent independent
native sessions. No SharpCompress/CLI fallback is permitted. If the pinned native
runtime cannot meet parallelism, report the gate and obtain authorization for
any material ABI/engine/plan correction rather than silently serializing copies.

Keep IGameCopyInteraction's operation/prompt identity. The collection owner routes
to each CopyOperationViewModel's own transient CopyInputViewModel and completion
source. Multiple password/overwrite/FR-19 decisions may coexist; masked password
text is cleared on answer, cancellation and disposal, never copied into snapshots,
results or logs. Request versions reject old buttons and late callbacks for that
row without discarding another row's state. Resolved entries, skipped entries,
combined progress and terminal output warnings remain per operation.

## Device removal, failure and aggregate shutdown

Maintain dependencies by captured source and destination device/volume. Device
loss/replacement fails all dependent nonterminal operations including waiters;
unrelated operations and their reservations remain valid. Changing selection,
preferences or a draft source/PSP never invalidates admitted requests. Native
root/generation changes still do; never retarget to a replacement at the same path.

Reserve removal under the coordinator admission gate before taking the affected
operation set; no new dependant can enter afterward. Signal failure outside the
gate, cancel resource/input waits and await all affected completions, including
callbacks/flush/handle settlement. Forward the *same* freshly revalidated removal
request through existing Spec 004 authorization/native policy. Rejection/veto
stays visible; do not resume failed operations or force removal. Repeated removal,
shutdown and completion interleavings must not release a reservation too early.

Reuse ApplicationDeviceLifetime's single close owner with an aggregate collection
view model. One confirmation covers every nonterminal row; opening it atomically
closes submissions without stopping current work. Decline reopens submissions
and preserves rows/decisions; accept keeps admission closed, cancels all owners
and awaits all rows/native calls/resources before detaching copy or disposing
the device session. Late startup/attachment, repeated close, a row finishing
while confirmation is open, and removal failures retain T09's controlled exit
and cleanup-error behavior. No active-row completion can reopen admissions.
No confirmation is needed when every row is terminal. Programmatic disposal
closes admission and drains unconditionally through the same ownership boundary.

## UI states and interactions

- Catalogue supports one/many selected games; Send for copying opens/uses per-game
  not-yet-submitted choices. Native device/source availability is visible.
  Prepared entries can be submitted together; unresolved games get individual
  incidents without reverting accepted rows. Retrying an incident is explicit.
- Submission choices show source reader/volume and one PSP. Multiple mounted
  optical sources are selectable per game; different drafts can use different
  readers. Existing preference projections may seed choices, but admitted rows
  have read-only captured choices and expose no retarget/edit command.
- The table uses a typed row template showing game, source, PSP/root, state,
  actual-byte combined progress and individual Cancel. Preparing is indeterminate;
  Waiting for destination file shows its reason, retains known progress and
  remains cancellable. Capacity rejection appears as its own failed row.
- Password, overwrite and FR-19 controls are bound within their owning row/detail
  area, never a global blocking modal. All may be answered independently. English
  text includes destination and retained-output warning, with Overwrite/Skip/
  Cancel per file and masked Retry/Cancel for password decisions.
- Completed/Completed with skipped files/Cancelled/Failed remain session rows;
  all-skipped completion still qualifies. Cancellation/failure warns about
  incomplete/incorrect output; completion requires release. No persistent history.
- One bounded aggregate close panel uses Keep application open / Stop copies and
  close. It explains all nonterminal copies stop and files remain. Buttons remain
  reachable at minimum size, including long paths and many simultaneous prompts.

Keep compiled bindings/x:DataType on every row/input template, AutomationProperties
names including game/operation context, visible state text in addition to color,
keyboard selection and focus order, and scrolling/wrapping at 900 × 450. Use the
explicit GameCopyView composition; if ViewLocator resolves a new model, cover its
actual namespace mapping. View code-behind has no queue, device or file policy.

## Test strategy and implementation gates

Write behavior tests first for each subsequently authorized increment and show
missing-behavior failures; reuse passing evidence only for its recorded scope.
Revise tests asserting global single-slot rejection/selection-change failure to
revision 0.6 expectations while retaining identity-change/no-fallback tests.
No test or production code changes occur during this planning phase.

1. Pure rules/contracts: partial submission/captured immutable values, actual
   pending reservations/overflow, ledger settlement/retry/skip/retained bytes,
   operation/request revisions and terminal outcomes. All FR/NFR mappings below
   require concrete evidence, not just model construction tests.
2. Deterministic coordinator/service integration: controlled pending reads/writes
   proving overlap on same/different PSPs, shared sources, second submissions,
   independent cancel/fail/input and no stale callbacks; synchronized capacity
   races; conflict wait then fresh consent; first writer failing/cancelling;
   directory conflicts and progress/terminal release boundaries.
3. Native archive and Linux/Windows filesystem: independent simultaneous native
   sessions, same-source independent offsets/parts/passwords, cancellation without
   cross-session damage, root/alias/short-name/case behavior and actual handle
   release. New-file namespace races, simultaneous directory creation, budget
   sampling/allocation ordering and Windows sharing barriers need actual native
   evidence; fakes do not prove them. Keep ABI/version/hash/source/licence checks.
4. Awaited Avalonia headless functional tests: catalogue multi-selection without
   batch CRUD, distinct optical choices, mixed-ready batch incidents, multiple
   typed rows/prompts, keyboard focus, minimum layout, responsive pending work,
   row-local stale responses and aggregate removal/close/startup races. Assert
   actual visible projection, not merely a Core revision.
5. Repository regressions: CRUD/CSV/SQLite/device ownership/authorization and
   architecture boundaries; source-part SHA-256 before/after concurrent success,
   cancellation/failure and repository guards proving no catalogue mutations.
   Injected I/O faults complement rather than replace physical evidence.

During authorized implementation run from the repository root:
`dotnet build PSPMan.slnx`, then `dotnet test PSPMan.slnx`. Read the run-tests skill
before testing. Measure owning Core/Infraestructure/Desktop line/branch coverage
sequentially with existing coverlet.collector; never instrument while another
process uses the same binaries, and rebuild afterward. Report significant gaps
without inventing a numerical threshold. Required-native environment guards must
make absent assets fail rather than turn mandatory execution into a silent skip.

On designated Linux/Windows hosts and disposable media, extend the existing
[manual matrix](./manual-validation.md) with concurrent same/different PSPs,
multiple actual optical readers/discs, same archive shared reads, simultaneous
admissions near capacity, same-file/case/alias conflict waits and per-file consent,
multiple input prompts, independent cancel/error and shared-device removal, and
all-operation close decline/accept/completion/startup races. Record OS/CPU/RID,
PSP model, volume/filesystem identity, package versions/hashes, exact procedures,
expected/actual payload/source hashes and logical catalogue state. Verify release
before removal/exit and that prior unrelated output remains unchanged.

All earlier Linux/Windows physical/native cases remain mandatory; add concurrency
cases rather than replacing them. Missing Windows/PSP/source/reader environments
stay unexecuted and blocked in pending-validation.md. Do not provision Windows,
format media or launch ordinary automatic device mounting against arbitrary
hardware. Graphical smoke uses explicitly designated media or a verified isolated
private bus/temporary catalogue, with scope disclosed. Forced process termination
is not graceful OS-session-shutdown evidence. Current deployment architectures
must be inventoried rather than silently limiting support to linux-x64.

## Requirement and acceptance traceability

| Requirement | Planned component/change | Verification / acceptance |
| --- | --- | --- |
| FR-1 | Catalogue multiple selection and explicit submission | Selection adds/writes nothing; individual/batch Send; AC-1 |
| FR-2 | Pure capture and submission-local PSP choices using Spec 004 | Missing/unready/multiple PSP, captured root and no fallback; AC-1 |
| FR-3 | Captured source evidence and existing confined archive access | Full Location, missing/unreadable and stable source across UI changes; AC-2 |
| FR-4 | Shared volume ledger, native accounting fence and retry settlement | Simultaneous capacity admission, actual-size mismatch, no double-count, per-volume release; AC-3 |
| FR-5 | Existing per-operation ordered engine/native adapters | Nested ZIP/RAR tree/hash under ISO while independent operations overlap; AC-4 |
| FR-6 | Target lease before current inspection and row-local consent | Per-file refuse/accept/reconfirm after wait, no stale overwrite; AC-5, AC-22 |
| FR-7 | Typed collection/row snapshots and combined actual-byte bar | Independent visible source/PSP/state/cancel and wait reasons; AC-6 |
| FR-8 | Per-operation terminal reducer and notifications | Completion after release, some/all skipped, unaffected other rows; AC-4, AC-10, AC-18 |
| FR-9 | Per-ID cancel and resource/file release | Wait/input/preparation/write cancellation retains bytes, only one row stops; AC-7, AC-18 |
| FR-10 | Operation failure and captured device dependency index | Extraction/write isolation, all loss dependants fail, no replacement writes; AC-8 |
| FR-11 | Multi-operation registry and admission/close gates | Overlap, ongoing additions, local waits, no numeric global limit; AC-9 |
| FR-12 | Existing unsafe/link classifier and pinned native roots | Skips/no outside writes during concurrent namespace races; AC-10 |
| FR-13 | Separate archive sessions and per-row password leases | Concurrent masked retry/cancel, no password reuse/leak between operations; AC-12, AC-24 |
| FR-14 | Per-session ordered multipart sources | Parallel same/different part sets, missing-part isolation and correct hashes; AC-13, AC-21 |
| FR-15 | Existing empty/definite-corruption classification per operation | Early no output/late retained output, other operation continues; AC-14 |
| FR-16 | Captured requests, dependent stop and removal admission reservation | UI choices do not stop copies; await all dependent releases before same removal; AC-15 |
| FR-17 | Aggregate close decision, closed admission and lifetime drain | Decline/accept/repeated/racing close covers all rows and native return; AC-16 |
| FR-18 | Native-equality claims and existing structural/name checks | Duplicates/case aliases, no simultaneous same-file writes, structural failure isolation; AC-17, AC-22 |
| FR-19 | Per-operation stopped invocation, retry/input and retained output | Wrong-password/damaged ambiguity, reacquire/reconfirm retry, no completed recopy; AC-19, AC-24 |
| FR-20 | Immutable per-submission requests | Same game to another PSP concurrently, waiting rows cannot retarget; AC-20 |
| FR-21 | Source-choice overlays and independent native/read handles | Shared archive/disc, multiple readers selected per game, no shared offsets/cancel; AC-21 |
| FR-22 | Native target claims plus short namespace guards | Conflict wait/reinspect/consent, cancel wait, parallel disjoint files and native aliases; AC-22 |
| FR-23 | Batch capture result with accepted IDs and per-game incidents | Mixed readiness admits ready games only, explicit incident resubmission; AC-23 |
| FR-24 | Per-row input owners and operation/prompt identity routing | Multiple prompts advance independently, stale/cross-row answers rejected; AC-24 |
| NFR-1 | Shared policy, platform adapters and Spec 004 boundaries | Linux/Windows native/physical evidence, paths and per-submission choices; AC-11, AC-15, AC-21 |
| NFR-2 | Off-UI independent runners and asynchronous leases/input | Responsive pending parallel work, catalogue selection/new Send/Cancel; AC-6, AC-7, AC-9, AC-24 |
| NFR-3 | English typed templates/documents and host path rendering | Compiled bindings, text, keyboard/minimum-size/platform roots; AC-11 |
| NFR-4 | Read-only sources and no catalogue writes from copy | Before/after source hashes and sealed repository guards for concurrent outcomes; AC-2, AC-7 |

Every completion criterion needs current tests/coverage, this traceability and
mandatory physical evidence, followed by separately authorized SDD code review
and final acceptance. No current single-copy result proves these new behaviors.

## Decisions, alternatives and risks

- Reuse the existing engine and evolve its coordinator, rather than putting
  scheduling into DevicesViewModel/DeviceSessionService. This preserves Core/
  adapter/UI boundaries and Spec 004 native policy; no generic scheduler needed.
- Per-operation immutable capture and local choice overlays replace a global
  selected-game/source pointer. Rejected: temporarily swapping session choices
  between starts; callbacks could then act on another submission's identity.
- Choose shared per-volume reservations and native file claims. Rejected: only
  checking free space independently or using raw-path keyed locks; both admit
  overcommit/alias races. Rejected: holding a volume lock for an entire transfer;
  it would serialize independent files contrary to the approved concurrency.
- Use one entry lease at a time and short namespace/accounting transactions,
  rather than reserving every archive filename upfront. This limits claim memory
  and avoids cross-archive lock cycles while preserving ordered extraction.
- No global numeric limit is introduced. Native worker/resource pressure may
  still cause ordinary per-operation errors; tests must demonstrate responsiveness
  and no head-of-line blocking from UI input. Do not promise throughput scaling.
- Filesystem aliases, buffered allocation visibility, cross-session engine safety
  and Windows directory sharing are implementation proof gates. No fake test,
  OS-derived comparer or progress-only ledger can substitute for those proofs.
- Do not share immutable progress maxima with attempt-level allocation counters;
  truncation/retry and already-written retained files have different accounting.
- Do not widen ABI/platform support or add dependencies silently if a proof gate
  fails. Propose a correction/revision with its exact behavioral impact instead.

## Historical evidence, downstream tasks and next approval

Previous implementation/verification remains linked for its single-copy scope:
[contracts](./core-transfer-contracts.md), [native compatibility](./native-archive-compatibility.md),
[archive adapter/package](./archive-adapter.md), [Linux filesystem](./linux-copy-filesystem.md),
[Windows filesystem](./windows-copy-filesystem.md), [engine](./copy-engine.md),
[coordination](./copy-session-coordination.md), [UI](./copy-screen-interactions.md),
[shutdown](./confirmed-shutdown.md), [coverage](./coverage.md),
[manual matrix](./manual-validation.md) and [pending native gates](./pending-validation.md).
T01/T03/T05/T06 Windows and T10 physical conditions remain open; Spec 004's
[pending device tests](../004-device-mounting/pending-validation.md) remain open.
Existing approvals and passing unaffected tests are not erased, but changed
single-slot/selection-stop expectations do not validate revision 0.6.

| Artifact | Required downstream follow-up after plan approval |
| --- | --- |
| Tasks 0.2 | Preserve identifiers/history and open native gates. Propose coherent concurrency increments for capture/registry, resources, native proofs, UI and aggregate lifetime; reassess T02/T04/T05/T06/T07/T08/T09 responsibilities without silently marking historical completion as new completion. Expand T10/evidence dependencies. Obtain task approval before implementation. |
| Test suites | Update only superseded single-slot/selection-change expectations; add deterministic cross-operation tests and actual native concurrency. Preserve archive/identity/confinement/retention and catalogue/device regression checks. |
| Coverage/manual/pending reports | Keep historical counts scoped; extend matrices and record current owning coverage, native version/package integrity and every missing environment. No report edit or execution is authorized by planning alone. |
| Spec 004 integration | Reuse discovery/authorization and one awaited removal interception boundary. Local submission choices do not change its global preference semantics or claim its native acceptance. Any actual required requirement change follows SDD change approval. |

This plan leaves tasks, tests, implementation, configuration and dependencies
unchanged. There is no commit/push or new implementation branch in this phase.
Future authorized implementation branches start from current local dev as
`feature/005_TNN_<description>`, preserving local changes without stash/reset.

Review outcome: approved specification 0.6's FR-1–FR-24, NFR-1–NFR-4 and
AC-1–AC-24 are mapped; no unresolved product decision or constitution conflict
was identified. Native/resource proof gates and historical unavailable hardware
remain explicitly unverified. Plan 0.3 is explicitly approved with “apruebo el
plan y autorizo preparar la revisión de tareas”. **Next: prepare tasks 0.3,
then obtain task-list approval and explicit authorization for the first task.**
No implementation or SDD validation is authorized by this approval.
