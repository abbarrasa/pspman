# T15 — Copy engine resource integration

Specification 0.6; approved plan/tasks 0.3. Implementation prepared; **native
Windows completion remains open**. This is implementation evidence, not formal
SDD validation or acceptance.

## Authorization and scope

“Apruebo el plan y autorizo a realizar T15” approved the presented scoped plan
and explicit unavailable-Windows dependency exception for T12/T14/T06 and their
earlier native prerequisites. Work started from clean local `dev` at `b38bf8c`
on `feature/005_T15_copy-engine-resource-leases`. No commits or pushes.
The exception permits available development/checks; it completes no dependency
and does not waive Windows or [T10 physical checks](./manual-validation.md).

One session-shared resource owner is now mandatory for engine calls, including
the existing single-copy Desktop composition. No public concurrent admission,
collection UI, aggregate removal or all-operation shutdown is enabled here;
those remain T16–T19. No catalogue, native ABI/runtime/package, provider, mounting,
authorization or persistence changes were made.

## Ownership and behavior

- Core's resource contract binds each caller-owned native destination using its
  captured volume evidence. Core never references Infraestructure or native
  handles. Desktop supplies one `CopyDestinationResources` to its engine;
  controlled tests inject their explicitly separate resource probes. There is
  no optional-resource/no-op path in the production engine.
- Inspect actual decompressed sizes and reserve the full pending file output
  before entry acquisition or destination mutation. Retry replaces the pending
  claim with full replay size, excluding completed entries. An insufficient or
  unknown capacity fails this operation before new writes, without a space wait.
- Acquire one entry before inspection/consent. Native contention publishes
  `WaitingForResource` with `DestinationFile`, without an obsolete prompt.
  After acquisition revalidate source/destination and inspect the current file;
  changed evidence still requires another conditional-open consent cycle.
- Retain the claim through input, conditional open, all writes, flush and output
  close. Directory entries release at completion; unsafe/link/declined entries
  release their pending bytes and any entry claim. No two entries are held by
  one operation. All-skipped completion retains its original meaning.
- The bound output stream already brackets actual chunks/partial failures with
  resource write epochs. The engine must not debit it again using progress.
  Monotonic per-entry UI maxima remain separate from per-attempt allocation;
  replay is charged again even when displayed progress does not decrease.
- After FR-19 stop, close output and release its entry and archive session before
  requesting an independent password decision. Explicit retry reinspects the
  archive, revalidates capacity and reacquires current target consent. Completed
  entries are not extracted twice. Definite corruption and I/O errors do not
  become password decisions.
- Finalization awaits output, entry, budget settlement, original destination and
  source release before terminal arbitration. Resource-owner faults remain
  failures even when their internal cancellation stops I/O. Generic release
  failures are classified as `CloseError`; typed resource failures are preserved.
  No deletion, rollback or credit for retained output is introduced.

## Verification map

| Scope | Evidence and requirement coverage |
| --- | --- |
| Core orchestration | Ten new controlled cases: actual-size reservation before inspection, claim held through output close, pending-byte release for unsafe/link/declined entries, explicit directory release before its nested file, retry of only incomplete work, published cancellable wait, failed budget/entry release and awaited settlement before native/terminal release. Supports FR-4/FR-6–FR-10/FR-12/FR-13/FR-19/FR-22/FR-24. These probes are not native acceptance. |
| Actual engine/native reader/filesystem | Thirteen scenarios on each prepared platform: different-file overlap on one destination using the same engine/reader/filesystem instance, same archive to independent synthetic PSP roots sharing the resource owner across engines, same-file waiting, retained failed/cancelled predecessors, waiter cancellation, changed-consent repetition, competing capacity, omission releasing capacity, owner-only flush failure, wrong-password and damaged-encrypted decisions allowing a second operation to overwrite the released file, and insufficient replay capacity retaining prior output. Supports FR-3–FR-10/FR-12–FR-15/FR-18/FR-19/FR-21/FR-22/FR-24; NFR-2/NFR-4. |
| Existing native service regression | Real ZIP, RAR, encrypted and multipart fixtures now run through mandatory shared resources. Conflicts, unsafe paths, duplicate entries, empty archives, missing parts, wrong-password retries, exact cancellation prefix and source hashes remain checked. |
| Presentation/composition | Controlled UI fixtures explicitly inject test resources. A new production-default composition case rejects a nonnative destination before output mutation. Existing close/input/device/catalogue flows remain under full regression. No multi-row UI acceptance is claimed. |

Linux uses actual temporary-root no-follow filesystem calls and the packaged
native archive reader. The temporary filesystem is tmpfs; capacities, pauses and
faults in designated cases are deliberately injected. Distinct synthetic PSP
identities/roots demonstrate engine/adapter isolation, not two physical PSPs.
Source SHA-256 values, exact retained bytes and post-result descriptor release
are checked; Windows rows additionally exercise root rename after handle release.

Test-first evidence: seven new behavioral cases failed against the original
engine with zero passes because resources were never used. After integration,
93 focused Core service/coordinator cases passed. Three later orchestration cases
extend directory/release-order/fault checks. Native overlap archives include
explicit shared-directory entries. An initial native consent probe rewriting the
same content did not establish a distinct target reliably; it now changes length
and verifies repeated consent. The replay-capacity probe accounts for the empty
file left by an earlier password-required attempt: no *additional* consent or
mutation is allowed after capacity fails.

## Commands and results

Run from repository root with .NET 10 and existing restored packages. The
`--disable-build-servers` option avoids the observed idle shared-server wait;
`--no-restore` uses unchanged dependency assets.

`dotnet build PSPMan.slnx --no-restore --no-incremental --disable-build-servers`
passed before and after coverage with zero warnings/errors. Final
`dotnet test PSPMan.slnx --no-restore --disable-build-servers` passed **1969**:
Core 771, Infraestructure 820 and Desktop 378; zero failures and **37 Windows
discovery skips**. The thirteen new Windows rows appear as one discovery skip on
Linux. No skipped row is counted as native execution. The final regression also
recompiled the last strengthened shared-engine overlap probe.

Coverage ran sequentially, with no other test/app using the instrumented
assemblies. Core's final focused run passed 55 cases; the native/resource focused
run passed 63 with 12 mandatory Windows discovery skips; Desktop composition,
flow and shutdown passed 58, including production-default confinement rejection.
The solution is rebuilt afterward before final regression. A complete rebuild
also avoids trusting incremental timestamps after editing a test during an
earlier compilation; the new composition case is present and executed.

| Production file | Covered lines | Covered branches |
| --- | --- | --- |
| `GameCopyService.cs` | 241/245 — 98.37% | 128/152 — 84.21% |
| `CopyDestinationResources.cs` | 368/393 — 93.64% | 159/195 — 81.54% |
| `CopyStartup.cs` | 17/20 — 85.00% | 11/12 — 91.67% |
| `WindowsCopyDestination.cs` | 0/161 — 0% | 0/64 — 0% |
| `WindowsCopyFileAccess.cs` | 0/242 — 0% | 0/168 — 0% |

These are focused owning-project Cobertura projections, merging repeated
generated classes by source line; they are not whole-feature coverage claims.
The remaining engine lines guard malformed callback/entry identities, missing
completion and invalid byte progress. Startup's unexecuted lines are its
default native inventory callback; controlled UI tests use explicit I/O seams.
Other owner gaps include defensive argument/identity/late-close and alternate
stream error paths. Windows code remains included and unexecuted; percentages
must not hide its mandatory native gate.

The required-Windows command targeting the new Windows matrix rejects Linux:
exit 1, one discovery failure, zero passes/skips, with “Mandatory Windows copy
tests require a native Windows runner.” This demonstrates the guard only.

Native X11 startup with `dotnet run --project
src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build --no-restore` passed using an
isolated catalogue/config/cache and a private D-Bus verified unable to activate
UDisks2. The owned visible window opened, resized to 900 × 450 and closed normally
with exit 0 and no application unhandled exception. Ambient portal/FUSE warnings
did not prevent startup; this is not physical-device or pixel-level validation.

## Mandatory Windows and physical restart

On a designated native Windows/.NET 10 host, stage the matching pinned engine
and bridge and verify its runtime manifest/source/notices/ABI first. Run:

```powershell
$env:PSPMAN_REQUIRE_WINDOWS_COPY_TESTS = '1'
$env:PSPMAN_REQUIRE_NATIVE_TESTS = '1'
dotnet build PSPMan.slnx
dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter 'FullyQualifiedName~GameCopyEngineResourceTests|FullyQualifiedName~GameCopyServiceIntegrationTests'
dotnet test PSPMan.slnx
```

All thirteen `Windows_engine_resource_matrix` rows and existing native service
rows must execute without skips. Run T12's independent-session, T14's native
resource cases and their T01/T03/T05/T06 prerequisites as documented separately.
Missing runtime, NTFS facilities, permissions or designated media are unmet
prerequisites, not passing tests. Portable/Linux substitutes cannot complete
these rows. T15 remains unchecked until actual Windows integration passes.

T10 must additionally exercise actual PSP/FAT behavior, aliases, free-space and
allocation visibility, DVD/multiple optical sources, disconnection/removal and
physical release. No device was mounted, copied to, ejected or provisioned by
this task. Native temporary-root tests and isolated GUI startup cannot certify
hardware behavior. Next implementation task T16 requires its own scoped plan,
explicit authorization and handling of still-open native dependencies.
