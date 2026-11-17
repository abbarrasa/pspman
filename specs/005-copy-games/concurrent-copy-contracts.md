# T11 — Concurrent submission and resource contracts

Specification 0.6; approved plan/tasks 0.3. Status: complete in T11's pure scope.
The user approved the presented decomposition and explicitly authorized T11 with
“Apruebo el plan y autorizo iniciar T11”. Branch:
`feature/005_T11_concurrent-copy-contracts`, started from local dev `17f6f00` with the
pre-existing specification/plan/tasks/memory edits preserved. No commit or push.
Only T11 is authorized; this evidence does not start T12 or waive native gates.

## Implemented boundary

- [Models](../../src/PSPMan.Core/Transfers/GameCopyModels.cs) add immutable
  submission/admission/incident records, a captured-request row envelope with
  revision, and a waiting state/reason. Existing enum values are preserved by
  appending new values; the snapshot wait reason is optional. Existing callers
  still compile. Operation IDs are caller-supplied, so pure capture performs no
  random generation, I/O or device selection side effects.
- [Submission rules](../../src/PSPMan.Core/Transfers/GameCopySubmissionRules.cs)
  resolve each game against one inventory with immutable local preference
  overlays, using the existing Spec 004 rules. Ready requests and individual
  incidents are returned separately in input order. Missing/unready/ambiguous,
  duplicate device/volume evidence and invalid paths do not silently fall back.
  A source volume cannot belong to a different explicit device; each PSP has
  one usable volume. Existing session associations/catalogue records are not
  mutated. Different optical readers and the same game sent to another PSP are
  represented by separate captured requests.
- CanApply checks captured request equality, increasing row revision, matching
  operation IDs and a nonterminal current row. It rejects delayed/cross-operation
  projection without changing existing prompt matching. Waiting is not password
  or overwrite input; an old input response cannot match that state.
- [Resource contracts](../../src/PSPMan.Core/Transfers/IGameCopyResources.cs)
  define asynchronous shared budget/entry ownership and per-write epochs. Native
  identity and allocation proof remain the adapter's responsibility. Target
  identity is opaque; a claim does not provide overwrite consent or replace a
  confined conditional open. Completion bookkeeping is not cancelled with the
  transfer. No delete/restore, native handle, UI or persistence API is added.

## Immutable capacity rules and integration obligations

[GameCopySpaceReservations](../../src/PSPMan.Core/Transfers/GameCopySpaceReservations.cs)
implements checked immutable transitions. The owner must atomically publish
returned states; separate concurrent branches are not an admission transaction.
This type is not a live native resource coordinator or a filesystem allocator.

A CopyVolumeKey includes captured device/volume identities and media generation,
not mount spelling. One operation cannot change its key. Each transition returns
a versioned claim; stale/released/replaced claims cannot debit, skip or release a
new claim. Versioning continues after release to avoid reuse of an older token.
Distinct volume incarnations have independent budgets; actual device validation
and stopping an old incarnation remain later coordinator/adapter responsibilities.

Reserve replaces an operation's pending amount, preserving its unsettled charge,
and counts other operations on that same volume exactly once. Unknown/negative
capacity, invalid pending size, overflow or insufficient space returns a typed
error with the exact original state and no new claim. Zero and exact-capacity
claims are valid. A failed replacement leaves the previous claim current.

RecordWritten moves accepted bytes from pending to unsettled without freeing
claim capacity. SettleWritten accepts the owner's attestation that written bytes
are reflected in a consistent held-volume free-space sample. SkipPending
releases only not-written work. Retry can replace pending bytes with the full
failed-entry replay size while already-written output still consumes real space.
Release requires closed operation files and no unsettled charge; it removes only
unused claims and never returns a fictitious increase in native free bytes.

T13/T14 must implement the admission fence, partial/uncertain write settlement,
actual alias equality and lease lifecycle. T15 integrates those resources into
the engine. The pure ledger cannot establish whether native allocation is
visible, whether two native names alias, or whether an output handle is closed.
The caller's attestation/consistent sample is an explicit contract, not inferred
from progress. Real-source/PSP parallelism and output write exclusion are not
claimed by these tests.

## Test-first evidence and traceability

[GameCopyConcurrencyRulesTests](../../tests/PSPMan.Core.Tests/GameCopyConcurrencyRulesTests.cs)
contains 27 cases. Initial compilation failed for missing T11 models/rules.
The first executable run passed 17 cases; three nullable-long theory rows failed
in xUnit argument binding rather than product behavior and were corrected to
long literals. An additional six-case readiness test then reproduced two missing
Ready-state checks; capture now preserves the existing copy admission readiness
requirement without changing device association policy. The complete focused
suite subsequently passed 27/27.

| Requirements / acceptance | T11 evidence and limit |
| --- | --- |
| FR-1–FR-3, FR-20–FR-21, FR-23; AC-1–AC-2, AC-20–AC-21, AC-23 | Partial capture, local optical choices, separate same-game destinations, immutable game/root values, unavailable/ambiguous choices, invalid IDs/paths/volume ownership and host path syntax. Actual submission UI and native reads remain later tasks. |
| FR-4; AC-3 | Per-volume pending/unsettled state, exact/unknown/invalid/overflow cases, replacement failure, write/settle/skip/retry/release and retained-space accounting. No native sampling proof. |
| FR-7, FR-9, FR-11, FR-22, FR-24; AC-6–AC-7, AC-9, AC-22, AC-24 | Wait/revision/prompt models, identity/version rejection and asynchronous lease contracts. No scheduler, real waiting/locks or UI cancellation implementation is introduced. |
| NFR-1, NFR-3, NFR-4 | Linux/Windows-shaped pure path cases, English artifacts, immutable source/catalogue values and existing boundary regression. Windows native and PSP evidence remain mandatory/unexecuted. |

## Verification results

On Linux x86_64, .NET SDK 10.0.404, xUnit 2/VSTest and existing coverlet.collector:

| Check | Actual result |
| --- | --- |
| `dotnet build PSPMan.slnx` | PASS; zero warnings/errors |
| `dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~GameCopyConcurrencyRulesTests` | 27 passed, zero failures/skips |
| `dotnet test PSPMan.slnx --logger trx --results-directory <scratch>/t11-regression` | 1877 passed, zero failures; 26 existing Windows discovery entries skipped |
| Owning project regression counts | Core 761; Infraestructure 739 + 26 skips; Desktop 377 |
| Focused Core coverage, collected only after regression ended | 27 passed; results below |

Coverage command:

```sh
dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --no-build --filter FullyQualifiedName~GameCopyConcurrencyRulesTests --collect:"XPlat Code Coverage" --results-directory <scratch>/t11-coverage
```

The owning Core Cobertura attachment reports distinct source lines, combining
compiler-generated classes by source line without counting method listings twice:

| Source | Lines | Branches |
| --- | ---: | ---: |
| GameCopySubmissionRules | 69/69 (100%) | 63/70 (90.00%) |
| GameCopySpaceReservations, including volume-key data | 66/66 (100%) | 36/38 (94.74%) |

This focused result is not total solution coverage or native coverage. Remaining
branches include unsupported syntax, invalid root/duplicate-volume evidence and
defensive reservation/absent-claim paths; no numeric threshold
is invented. Interfaces have no executable adapter behavior to cover. The whole
regression includes existing architecture checks; Core gained no package or
platform/UI/persistence reference. Coverage ran sequentially; a final rebuild
passed with zero warnings/errors afterward, leaving uninstrumented outputs. No UI startup was needed
because Desktop/composition/UI code was not changed; no physical test was run.

## Next step and preserved gates

This is additive Core work. Existing GameCopyService, session coordinator,
production composition and UI behavior are unchanged; concurrency is not
activated. No package, device-provider policy, schema, native code or user-media
write is included. T01/T03/T05/T06 Windows, new native concurrency/resource gates
and T10 physical evidence remain open. The next task needs its own presented
scope and explicit authorization, including any required dependency exception.
