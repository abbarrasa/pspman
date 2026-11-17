# T06 — Single-operation copy engine

Specification 0.5, plan/tasks 0.2. Implementation prepared; Windows native
integration remains unexecuted. This report is implementation evidence, not
formal SDD validation or physical acceptance.

## Authorization and scope

After asking that each task have its own implementation plan for approval,
the user requested the next task. A scoped T06 plan was presented covering the
Core service, tests, retention, progress, password retry, regression and memory.
It explicitly asked permission to proceed while T03/T05 Windows checks remained
open. The user's “Apruebo el plan” approved that scope and dependency exception.
It did not waive those checks or authorize T07. The branch was created from
clean local dev at the start of this task:
`feature/005_T06_single-operation-copy-engine`. No commit or push was performed.

## Implementation

[GameCopyService](../../src/PSPMan.Core/Transfers/GameCopyService.cs) uses only
existing Core archive, filesystem and interaction contracts. Each invocation
owns its source/destination/archive/output sessions, captured source token,
password references, prompt identities, logical progress and terminal gate.
The later T07 coordinator owns the single active slot and device observation.
No repository, UI, native API, queue or new dependency was introduced here.

- Inspect first and use checked actual archive sizes rather than catalogue
  SizeBytes. Unknown/insufficient free space prevents destination mutation.
  Revalidate source/destination evidence before extraction and entry mutations.
- Extract selected entries in archive order under the destination's `/ISO`.
  Preserve directories; omit unsafe paths, links and declined overwrites.
  Native destination inspection controls names, structural conflicts and case.
- Ask per-file overwrite consent, including duplicate archive paths. Conditional
  opening that reports a changed target causes fresh inspection and consent.
- Keep one monotonic decompression/write progress value. Skips resolve logical
  work without claiming written bytes; per-entry maxima avoid double-counting
  retries. Only successful release of every owned resource permits 100%.
- On password-required/incorrect/indeterminate errors, await the native return,
  close outputs and archive, and retain source/destination sessions and bytes
  while awaiting an explicit retry/cancel response. Reopen after revalidation,
  compare entries, check remaining space and request only unfinished indices.
  The adapter replays required solid prefixes without output callbacks. Failed
  entries use normal overwrite consent; completed entries are not copied twice.
- Ignore mismatched prompt identities. Cancellation can abandon an unresponsive
  interaction without accepting its eventual response. Publish stopping before
  closing failed/cancelled entries and remove prompts from terminal snapshots.
- Cancellation/failure closes owned resources without any deletion/restoration.
  Close failure takes precedence over clean cancellation. Terminal results warn
  about retained incorrect/incomplete files. Untyped exceptions use a generic
  public message so interaction exception text cannot expose password input.

## Verification and traceability

| Requirements | Evidence in this task |
| --- | --- |
| FR-3, FR-4, NFR-4 | Opaque source token checks, actual-size mismatch in both directions, exact/insufficient/unknown capacity, changed source/retry inspection, unchanged fixture hashes and no catalogue dependency. |
| FR-5, FR-14 | Real ZIP/RAR/solid/encrypted/multipart/split-ZIP extraction, nested file hashes, missing-part errors; ordered callback fake batches. |
| FR-6, FR-18 | Individual decline/accept/cancel, duplicate paths, target-change reconfirmation, incompatible names and structural conflicts; real duplicate/conflicting ZIP entries. Native filesystem case semantics remain T04/T05 evidence. |
| FR-7, NFR-2 | Immutable preparing/input/transfer/stopping/terminal snapshots and monotonic combined actual-byte progress; pending inspection, writes and prompts can be cancelled. Actual UI responsiveness belongs to T08. |
| FR-8, FR-12 | Unsafe/link/all-skipped outcomes, declined existing files untouched; real traversal fixture has no outside mutation. |
| FR-9, FR-10 | Exact retained bytes for partial failure/cancel/overwrites; close errors, callback exceptions, final-release cancellation, cooperative and late/unresponsive prompts; real native cancellation retains an exact payload prefix. |
| FR-13, FR-15, FR-19 | Password retries/dismissal, stale input rejection, empty/early corruption, late corruption/missing-part/write faults; real wrong-password and damaged-encrypted inputs show the same ambiguous decision with closed writable streams and identical retained hashes after cancellation. |

### Test-first evidence and corrections

The initial focused build failed solely because GameCopyService did not exist,
after correcting two test scaffold compilation errors. The first implementation
passed 22 service cases; expanded tests covered remaining retry and lifetime
behavior. Further RED tests reproduced a prompt remaining in the terminal
snapshot, cancellation waiting for noncooperative input, raw interaction exception
text exposure and missing stopping publication. The service changes made those
cases pass before final verification.

Initial integration assertions incorrectly treated held read-only identity pins
as writable output files, and assumed a wrong password necessarily emitted
nonempty bytes. They now inspect captured output streams' CanWrite state and
compare the exact retained hashes, permitting zero-length retained files.
A nonexistent encrypted fixture name caused the initial full regression's one
failure (689 Core, 738 Infraestructure, 312 Desktop passed; 26 Windows skips).
That name was corrected and the test assembly was rebuilt for final regression.
These initial failures are not passing evidence.

### Reproducible commands

The projects use xUnit v2 and Microsoft.NET.Test.Sdk/VSTest on .NET 10.
Run sequentially, never instrumenting a shared assembly while another test uses it:

```sh
dotnet build PSPMan.slnx
dotnet test PSPMan.slnx
dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~GameCopyService --collect:"XPlat Code Coverage" --results-directory <scratch-results>
dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter FullyQualifiedName~GameCopyServiceIntegrationTests
```

Focused Core verification: **45 passed**, including an instrumented run.
Scoped service-file coverage: **213/220 executable lines (96.82%)** and
**119/140 branch outcomes (85.00%)**, calculated from the Cobertura source-file
line union and per-line branch counts across generated async classes. No
production coverage exclusion or threshold was added. Seven uncovered lines
are defensive invalid-index/incomplete-callback/oversize-progress rejection and
explicit directory creation in the fake-only coverage run; real archive tests
exercise directories separately without instrumentation. Missing branch outcomes
include defensive callback/adapter contract violations, native late skips and
invalid interaction decisions. This is service coverage, not device/Windows/UI
coverage; acceptance is not inferred from these percentages.

Final `dotnet build PSPMan.slnx`: **passed, zero warnings/errors**.
Final `dotnet test PSPMan.slnx`: **1744 passed, zero failures, 26 Windows
discovery skips** (Core 693, Infraestructure 739/26 skips, Desktop 312).
This includes all 45 new Core service cases and all 18 Linux service integration
cases. The 26 skips comprise four existing device cases, 21 T05 discovery
entries (24 prepared native cases), and one T06 theory (four prepared rows).
Native Windows session coverage remains zero. Editor diagnostics for the four
new C# files and final whitespace checks report no errors.

## Mandatory outstanding verification

The Linux integrations use native archive and filesystem adapters against only
designated temporary directories and synthetic device observations. They do not
prove real USB PSP detection/mount/removal, real device space exhaustion,
physical disconnection, FAT behavior, privilege prompts or UI/close integration.
Those remain with T10 and the earlier native/device prerequisites.

The four Windows engine integration cases are prepared behind WindowsCopyTheory
but have **zero native executions** in this session. Linux reports their theory
as one skipped discovery entry. T06 remains unchecked pending that platform
integration evidence; its dependency exception does not complete T03/T05.
Use a Windows .NET 10 runner with the matching packaged archive runtime, then:

```powershell
$env:PSPMAN_REQUIRE_WINDOWS_COPY_TESTS = '1'
$env:PSPMAN_REQUIRE_NATIVE_TESTS = '1'
dotnet build PSPMan.slnx
dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter FullyQualifiedName~Windows_engine_copies_real_archives_and_releases_all_handles
dotnet test PSPMan.slnx
```

All four rows must execute without platform/native skips; record OS,
architecture, filesystem, archive assets, hashes and actual handle-release
results. Build/stage the Windows engine/bridge first under T03's procedure and
complete T05's remaining checks. Keep T06 open if the runtime is unavailable or
any required case fails. No graphical startup was performed: this task changes
no UI and normal startup would enable hardware discovery/automatic mounting.
T07 has not been started or authorized. Present its scoped plan and obtain
approval before any later implementation.
