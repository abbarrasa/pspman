# T16 — Captured multi-operation ownership

Specification 0.6; approved plan/tasks 0.3. Scoped Core implementation evidence;
verification complete for T16's scoped completion criteria. This is not formal SDD validation or physical/native
Windows acceptance.

## Authorization and boundary

“Apruebo el plan y su implementación” approved T16's presented plan and explicit
exception allowing work despite T15's unavailable native Windows validation.
The checkout was clean local `dev` at `e23c2f8`; work uses
`feature/005_T16_multi-operation-coordinator`, without commits or pushes.
The exception does not complete T15 or T01/T03/T05/T06/T12/T14 Windows gates;
[physical T10](./manual-validation.md) remains mandatory.

Only Core ownership and affected behavioral tests change. The engine and shared
native resources from T15 are reused; no native adapter/provider, mounting,
authorization, runtime/package, catalogue or persistence change is introduced.
The existing one-row UI continues through a compatibility entry point. Production
batch admission, row-specific presentation and aggregate application close remain
T17–T19 and are not activated by this task.

## API and ownership

- `Submit` materializes caller enumeration outside the registry gate, captures
  all requests against one immutable device-session observation, and registers
  every accepted owner/completion before scheduling runners outside the gate.
  It returns accepted requests/IDs and indexed per-game incidents. Missing,
  ambiguous, failed-provider, duplicate-operation, removal and close conditions
  cannot silently choose another device or roll back ready games.
- Each owner retains its immutable request, independent cancellation/completion,
  sequenced `GameCopyOperationSnapshot` and terminal arbitration. `Operations`
  returns immutable session-only projections; `GetCompletion(id)` exposes the
  authoritative completion. IDs remain reserved for the session, including after
  completion. Terminal owners release observers and cannot be revived by stale
  cancellation or input/progress belonging to another ID.
- `CancelAsync(id)` and `FailAndStopAsync(id, error)` affect only that active
  owner and await engine return/file/resource release. Stale IDs are harmless
  stop commands. `CancelAllAsync` drains the current set without itself closing
  admission; callers needing exclusion must acquire the close gate first.
- Selection/preferences are draft inputs only. Active validation checks captured
  provider/device/volume identity, generation, capabilities, mounted root and
  readiness. It preserves the device session's normalized inventory contract and
  never re-resolves an admitted request using new choices.
- A device dependency index contains all active owners using it as source or
  destination. Whole-device removal reserves admission first, fails/signals all
  dependants outside the gate, awaits every completion, revalidates the same
  observation and forwards the existing Spec 004 request. Caller cancellation
  does not abandon file release. Independent operations/submissions continue;
  the reservation remains through device-session acceptance of removal.
- `BeginClose` returns one shared decision identity and closes admission without
  stopping work. `ResolveCloseAsync` rejects obsolete identities; decline reopens
  admission, acceptance permanently closes it, reserves one shared drain before
  callbacks, stops all owners and awaits them plus outstanding removal handoffs.
  A completion cannot reopen admission and a late decline cannot undo acceptance.
- Unconditional `DisposeAsync` reserves its common task before cancellation,
  detaches observation, closes admission and drains all owners/removals. It does
  not dispose its caller-owned device session. Registry locks are never held
  across I/O, cancellation, observer callbacks or awaited completion.

A terminal immutable snapshot is stored before completion is signalled. Observer
notification can follow completion, so consumers use the stored projection for an
immediate authoritative read and `GameCopySubmissionRules.CanApply` for queued
sequenced UI delivery. Consumer failure affects its operation, not another row;
a failed terminal consumer cannot strand completion or resource/removal ownership.

## Compatibility and unchanged responsibilities

`StartAsync`, `Snapshot`, parameterless cancel/fail and `SetSelectedGame` retain
single-row composition compatibility. Only this legacy start rejects another
active operation; the registry's `Submit` admits independent work during other
operations' preparation, input, resource waits, transfers or stopping. The legacy
projection is tied to its own operation ID rather than whichever row reported
last. These compatibility APIs are removed/replaced when T19 wires batches.

The superseded selection-stop expectations change: switching the focused game,
source preference or PSP preference keeps the accepted request/input alive.
Existing identity, no-fallback, file-close, stale removal, cancellation, provider
failure and shutdown tests are retained. Cancellation/failure still retains all
written/overwritten bytes; no deletion, restoration or format conversion exists.

## Traceability and controlled verification

| Behavior | Requirements / acceptance scope | Evidence |
| --- | --- | --- |
| Synchronous capture, partial admission, local optical reader/PSP choices, same game to another PSP | FR-1–FR-3, FR-11, FR-20, FR-21, FR-23; AC-1, AC-2, AC-20, AC-21, AC-23 | Mixed-ready batch, immutable enumeration/preferences, two explicit optical readers, duplicate-ID races and unavailable-provider rejection. |
| Independent owners, real async engine overlap, pending work and input | FR-7, FR-9–FR-11, FR-24; AC-6–AC-9, AC-24; NFR-2/NFR-4 | Two held output callbacks on one engine, additions during preparation/password/overwrite/resource wait/output close, independent password identities and cancellation, consumer failure isolation, per-owner revisions. |
| Device loss/removal and release before native forwarding | FR-10, FR-16, FR-20; AC-8, AC-15, AC-20; NFR-1 | Shared source/destination loss including resource waiters; two held output closures; new dependant rejected while independent work proceeds. Earlier device/generation/volume/capability/provider/removal-race cases remain. |
| Close admission, drains and reentrancy | FR-9, FR-11, FR-17; AC-7, AC-9, AC-16 | Completion during a pending decision, stale/declined/accepted identities, shared accepted drain, reentrant disposal and cross-thread observer/enumerator access without a held registry lock. |

Tests use the real Core coordinator and engine with explicitly controlled source,
archive, interaction and resource probes. They are deterministic ownership proof,
not actual optical drives, PSP media, native alias/capacity/flush or removal/UAC
proof. [T15 native engine evidence](./copy-engine-resources.md), [T12 native
sessions](./parallel-native-archive-sessions.md) and their outstanding Windows
rows remain separately authoritative.

Test-first evidence: the three selection-preservation cases failed against the
original coordinator. During setup, an independent source scenario initially
paired a different reader with the previous reader's volume; it was corrected
to use a consistent captured pair. An exploratory duplicated-identity scenario
was withdrawn: Spec 004 deliberately normalizes repeated keys before publishing,
so the experiment did not exercise a coordinator-visible ambiguity. That existing
policy and the original identity validation remain unchanged. Rebuild after test
edits before accepting results; incremental timestamps can retain a stale binary
when an edit is saved while an earlier compilation runs.

Two existing close/input rows also expected changing selection to terminate a
copy. The first full run was interrupted while those obsolete expectations
awaited implicit cancellation; they now verify preserved input through decline
followed by explicit cancellation and rejection of the retired retry button.
All 58 focused composition/flow/shutdown cases then passed.

## Verification results and remaining gates

`dotnet build PSPMan.slnx --no-restore --no-incremental --disable-build-servers`
and the post-coverage `dotnet build PSPMan.slnx --no-restore
--disable-build-servers` passed with zero warnings/errors. No dependency changed;
`--no-restore` uses the existing restored assets and `--disable-build-servers`
avoids reusing unrelated build servers. Tests use xUnit 2/VSTest on .NET SDK
10.0.404, so positional project/solution paths and `--filter` are appropriate.

Sequential focused Core coverage passed 65 cases: 24 new registry cases plus
41 retained/updated coordinator cases. `GameCopySessionCoordinator.cs` covers
293/304 source lines (**96.38%**) and 218/259 branches (**84.17%**), using the
owning-project Cobertura projection merged by source line across generated
classes. Remaining gaps are defensive close/disposal-drain exception handlers,
cancellation-source disposal/callback-failure races, late failure progress
clamping and a legacy invalid-path guard. These gaps remain reported rather than
excluded or replaced by assertions of native coverage. SHA-256 checks confirm
both the test-output and Core-output assemblies were restored byte-for-byte
after instrumentation, before the post-coverage build and final regression.

Native X11 startup/resize/normal close passed with an isolated temporary
catalogue/config/cache and private D-Bus verified unable to activate UDisks2.
The owned visible window opened, resized to 900 × 450 and closed with exit 0,
without an application unhandled exception. No actual-device discovery, mount,
copy, ejection or provisioning was performed. This is not physical acceptance.

Final `dotnet test PSPMan.slnx --no-build --no-restore --disable-build-servers`
passed **1,993 tests**, with zero failures and **37 Windows discovery skips**:
Core 795, Infrastructure 820 and Desktop 378. Editor diagnostics, whitespace
and documentary link checks passed. No unavailable
Windows or physical check counts as passing. T17 needs its own scoped plan and
explicit authorization; formal SDD validation remains unauthorized.
