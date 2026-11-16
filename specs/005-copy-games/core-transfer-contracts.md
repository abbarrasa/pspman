# T02 — Core transfer contracts and pure rules

Specification 0.5, plan/tasks 0.2. The user explicitly deferred unavailable T01
Windows tests and authorized the next task. That exception permits this pure
Core increment; T01 remains open and Windows/physical acceptance is mandatory.
Branch: `feature/005_T02_transfer-contracts-rules`, created from local dev at the
same commit as the previous T01 branch. All pre-existing tracked/untracked work
and local deletions were preserved. No commit, stash, merge or push was performed.

## Implemented scope

- [Models](../../src/PSPMan.Core/Transfers/GameCopyModels.cs): captured game/device/
  volume identity, media generation, roots and operation/prompt IDs; ordered
  archive entries with actual sizes; native target evidence; typed errors,
  skips, outcomes and immutable progress/snapshots. Passwords appear only in the
  transient interaction response, not requests, snapshots, progress or results.
- [Archive contracts](../../src/PSPMan.Core/Transfers/IGameArchiveReader.cs): borrowed
  source lifetime, caller-owned read-only/seekable part streams, immutable entry
  inspection and source evidence, indexed extraction-target preparation/completion
  callbacks, controlled output streams or explicit skips. Completion callbacks
  close current output on failures before extraction returns. No native pointers,
  engine package or destination-path extraction API is exposed to Core.
- [Filesystem contracts](../../src/PSPMan.Core/Transfers/IGameCopyFileSystem.cs):
  identity-bound sessions, free bytes, non-mutating inspection and conditional
  exclusive-create/overwrite with exact consent evidence. Changed evidence returns
  without mutation; unsafe traversal returns a skip. No delete/restore API exists.
- [Interaction contract](../../src/PSPMan.Core/Transfers/IGameCopyInteraction.cs):
  cancellable per-file confirmation and password/retry responses with prompt IDs.
- [Pure rules and terminal gate](../../src/PSPMan.Core/Transfers/GameCopyRules.cs):
  unsafe/link classification; checked actual totals and capacity; native-fact name/
  structural decisions; renewed consent; combined monotonic work progression;
  skipped completion; stopped encrypted-error input states; stale-response
  rejection; atomic cancellation/completion arbitration after stream closure.

Portable path checks reject absolute/drive/UNC/device paths, traversal, empty
segments and NUL; directory trailing separators are accepted. Separator handling
preserves nested archive structure. Name compatibility and case identity come from
the destination adapter, not the process OS. Non-drive colons are not universally
rejected: a destination that disallows such names reports an incompatible-name
failure. Unsafe/link skips take priority over ordinary name/conflict errors.

Actual size never substitutes catalog SizeBytes or subtracts expected overwrite
credit. Empty archives differ from zero-byte files and directory-only archives.
Progress uses decimal arithmetic up to Int64 totals, with resolved skip/replay
work separate from actual decoded/written bytes. Retry cannot regress displayed
progress; unresolved or unclosed work cannot publish successful 100% completion.
A pending cancel can become failure if closing fails before terminal publication;
late/repeated requests cannot reverse a published result.

These are contracts and pure decisions, not filesystem confinement, decompression,
copy orchestration, device coordination or UI implementation. Platform adapters
must still prove handle-based containment/identity and actual case/name semantics.
The terminal gate arbitrates one operation only; it is not a queue or scheduler.

## Tests-first and results

[GameCopyRulesTests](../../tests/PSPMan.Core.Tests/GameCopyRulesTests.cs) initially
compiled and failed 23/23 because the rules were unimplemented. Added tests then
exposed close-failure priority, host-independent colon handling and near-completion
progress monotonicity; each failed for the absent behavior before correction.
The final suite contains 30 cases, including 100 two-worker stop/completion races.
No source/game files or PSP volumes are accessed by these rule tests.

Executed from the repository root using .NET 10.0.404, xUnit 2 / VSTest:

```sh
dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~GameCopyRulesTests
dotnet build PSPMan.slnx
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx --logger 'trx;LogFileName=t02-regression.trx'
git diff --check
```

Build passed with zero warnings/errors. Final full regression: **1,554 passed,
4 skipped** (Core 648, Infraestructure 594, Desktop 312). All 30 new rule cases
and 49 existing Linux native bridge cases passed; native assets were required
rather than silently skipped. The four existing Windows native device tests
remain skipped on Linux. Editor diagnostics
for all new C# files report no errors. Existing assembly/boundary checks verify
Core's framework-only references; no project/package changes were required.
No graphical startup is needed for this Core-only increment and no ordinary
hardware discovery or PSP I/O was launched.

## Requirement mapping and limits

| Requirement | T02 enabling evidence |
| --- | --- |
| FR-3 | Captured immutable source/volume/root/location and read-only source contract; native availability checks remain adapter work. |
| FR-4 | Unknown/negative/overflow actual totals, zero/exact/insufficient/unknown capacity and no catalog-size fallback. |
| FR-7 | Bounded monotonic combined progress, actual byte counters, skips/replay and no premature completion. |
| FR-8 | Closed-stream completion and skipped-files outcome, including all-skipped work. |
| FR-9, FR-10 | Atomic stop/completion race, close-failure priority, typed I/O failures and no cleanup/restore contract. |
| FR-11 | Nonterminal pending-password state and per-operation gate; global single-slot coordination remains T07. |
| FR-12 | Unsafe/link classification and unsafe-target skip priority; native containment still requires T04/T05. |
| FR-18 | Duplicate entries retain independent indices; native target identities drive consent; incompatible names/file-directory conflicts fail. |
| FR-19 | Indeterminate encrypted errors wait for a new password decision only after current outputs close; definite I/O/corruption remain failures. |
| NFR-3 | English technical models/messages/docs; captured platform path syntax reused. |
| NFR-4 | No I/O/persistence mutation in rules; passwords absent from published snapshots/results; adapter byte-identity evidence remains separate. |

T02 is complete with that passing regression. It does not implement
or accept the whole feature, mark pending Windows tests passed or authorize T03.
