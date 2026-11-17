# T14 — Shared Windows destination resources

Specification 0.6; approved plan/tasks 0.3. Status: **implementation and native
tests prepared; mandatory Windows execution open, T14 incomplete**.

## Authorization and scope

The user replied “Apruebo el plan y autorizo inciar la tarea” to the presented
T14 plan and explicit T05 Windows dependency exception. Work started from clean
local dev `de202c0` on `feature/005_T14_windows-destination-resources`. The
exception permits implementation and available Linux checks; it does not waive
T05/T14 native execution or physical T10 acceptance. No Windows endpoint was
established and no provisioning or arbitrary device access was performed.

No UI, copy engine/coordinator activation, device provider/mount policy, native
archive bridge, catalogue, package dependency or schema change is included.
Existing single-copy composition remains active until later integration tasks.
No removable-media I/O, graphical startup, commit or push was performed.

## Shared protocol and native adapter

[ICopyResourceDestination](../../src/PSPMan.Infraestructure/Transfers/ICopyResourceDestination.cs)
is internal to Infraestructure. It supplies captured volume evidence, held native
root identity, current native target lookup/creation and settled capacity samples.
Core's existing contracts remain unchanged. Both confined platform destinations
implement it; [CopyDestinationResources](../../src/PSPMan.Infraestructure/Transfers/CopyDestinationResources.cs)
reuses T13's ledger, short admission/namespace guards, cancellable entry claims,
chunk accounting, drain/settlement and owning-budget fault attribution.

The Linux implementation retains syncfs, openat2/statx and its existing semantics.
The Windows implementation in
[WindowsCopyDestination](../../src/PSPMan.Infraestructure/Transfers/WindowsCopyDestination.cs)
uses GUID-volume/file identity from its bound root and existing directory-relative
no-follow opens. Root/parent/reparse validation, per-directory native case flags,
hard-link skips, structural/name errors and evidence-checked overwrite remain
final authority. No host string comparer establishes path/alias equivalence.

Missing leaves are created exclusively after actual-size admission under the
short namespace guard. A metadata handle is opened while the creation handle
still protects the new object, and their identities must agree. The write-access
creation handle is then closed: retaining it as an identity pin would prevent
the next operation's writer after claim release. Metadata pins keep per-binding
FAT identity-reuse tokens while permitting the existing conditional writer open.
Native lookup gives long/short aliases and case-equivalent names the same target
claim; the Windows native tests must still prove that behavior.

The platform-neutral target record can carry complete opaque consent identity.
Windows' per-binding pin token therefore participates in the own-empty-placeholder
check rather than being stripped or compared as a path suffix. Only an unchanged
file created by that claim receives the exemption; a creation collision is an
ordinary existing file requiring consent. As in T13, stopped accepted creation
can retain an empty file and nothing is deleted/restored.

## Allocation sampling and output lifetime

Before capacity admission, the shared fence drains allocation epochs and flushes
managed output buffers. Windows then flushes each still-owned output file handle
before the existing held-volume full-size query. Output closure also performs
the flush before releasing its handle. There is no administrative volume flush,
privilege escalation or drive-path free-space fallback.

[CopyAccessStream](../../src/PSPMan.Infraestructure/Transfers/CopyAccessStream.cs)
adds an optional synchronous native synchronization callback inside its existing
I/O/close gate, after the managed buffer flush. Disposal waits for that callback
to return. Sync failures retain WriteError/CloseError classification and still
close/release the owned stream. The Linux callback remains absent; its capacity
barrier continues to perform filesystem synchronization separately.

[WindowsCopyNative](../../src/PSPMan.Infraestructure/Transfers/WindowsCopyNative.cs)
adds FlushFileBuffers; output opens request GENERIC_WRITE plus the existing data
read access needed for conditional hash checks. Writer sharing remains read-only,
so write/delete replacement barriers are preserved. Read/metadata modes retain
their independent-sharing policy. No broad write/delete sharing is introduced.

The [Microsoft FlushFileBuffers contract](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers)
requires a write-capable handle; the access-mask change explicitly satisfies that
documented contract. The existing full-size query uses the documented
[NtQueryVolumeInformationFile information class](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntqueryvolumeinformationfile).
These references guide the implementation; they do not prove the unexecuted
Windows allocation/flush/share behavior. A successful Linux managed seam cannot
certify Windows syscall permissions, quotas, caching or real FAT allocation.

## Test-first and available evidence

Two portable tests initially failed because the internal protocol was absent and
the shared owner retained Linux-specific destination/target fields. Those
architecture checks now pass. Three managed synchronization tests cover ordering,
error/close release and disposal waiting for a held callback; a portable native
access/share-plan test covers the added write right and preserved share masks.
They are **six portable cases**, not Windows execution.

[WindowsCopyDestinationResourcesTests](../../tests/PSPMan.Infraestructure.Tests/WindowsCopyDestinationResourcesTests.cs)
prepares **20 real Windows rows**, represented by ten skipped discovery entries
on Linux. They use actual temporary files/native calls with synthetic provider
observations and controlled output/capacity faults, never a fake Windows backend:

- Two independent writers held concurrently, merged parent directory and active
  file/root replacement barriers.
- Case-insensitive and actual 8.3 alias conflict waits, cancellation, retained
  bytes, own current consent and changed-target reconfirmation.
- Enabled case-sensitive directory with separate case-only native target IDs.
- Existing hard-link/junction skips, unsafe traversal, structural/name failures
  and unchanged sibling data.
- Before/after-allocation sample races, buffered output flush and full retry
  reservation without self-double-counting.
- Known/unknown partial write, flush/close failure and cancellation, exact retained
  bytes, exclusive file reopen and root rename after complete native release.
- Independent source handles/offsets and unchanged archive hashes.
- Changed-generation admission without output and a late external creation race
  that must not receive the own-empty exemption.

The existing Windows required-execution attributes are reused. No missing native
case is silently replaced by a portable check. The focused available run passed
38 cases (six new portable plus T13's 32 actual Linux resource cases) and skipped
ten new Windows discovery entries.

Host: Linux x64, .NET SDK 10.0.404, xUnit 2/VSTest. `dotnet build PSPMan.slnx`
passed with zero warnings/errors. Full
`PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx --logger trx` passed
**1945 cases**: Core 761, Infraestructure 807 and Desktop 377; **36 Windows
discovery entries skipped** (26 prior plus ten new), zero failures. Required
Linux native archive cases executed rather than skipping.

The command below intentionally failed on Linux with ten discovery failures,
zero passes/skips and the explicit native-runner requirement. This is **guard
rejection evidence only**, not Windows execution or an implementation RED:

```sh
PSPMAN_REQUIRE_WINDOWS_COPY_TESTS=1 dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --no-build --filter FullyQualifiedName~WindowsCopyDestinationResourcesTests
```

Sequential focused coverage passed 38 cases with ten new Windows discovery
entries skipped, using the same three-class portable/Linux filter plus
`--collect:"XPlat Code Coverage"` and `--no-build` after the regression finished.
Windows source files were kept in the coverage denominator:

| Managed source | Covered lines | Covered branches | Meaning |
| --- | --- | --- | --- |
| CopyDestinationResources | 365/393 (92.88%) | 159/195 (81.54%) | Shared owner exercised through Linux; Windows opaque-evidence branch and defensive/lifecycle alternatives remain gaps. |
| CopyAccessStream | 41/70 (58.57%) | 20/40 (50.00%) | Focused output/flush/close and callback evidence; read/seek/synchronous facade paths are largely outside this filter. |
| WindowsCopyDestination | 0/161 (0%) | 0/64 (0%) | Native destination unexecuted. |
| WindowsCopyFileAccess | 0/242 (0%) | 0/168 (0%) | Native root/source/output integration unexecuted. |
| WindowsCopyNative | 9/133 (6.77%) | 18/126 (14.29%) | Portable access/share plans only; zero Windows syscall execution. |

These are focused managed source figures, not full-solution/native Windows
coverage or a certification threshold. The post-instrumentation
`dotnet build PSPMan.slnx` passed with zero warnings/errors. Final diff/new-file
whitespace checks, relative documentary links and editor diagnostics passed.
No remaining available check is pending; mandatory Windows and physical checks
remain unexecuted and T14 stays open.

## Mandatory Windows restart procedure

T14 remains unchecked until the real Windows matrix passes. This Linux x64 host
has no established Windows runner/toolchain; previous Windows deferrals do not
complete T05 or authorize acceptance. Keep earlier T01/T03/T05/T06/T12 and Spec
004 requirements separate in [pending validation](./pending-validation.md).

1. Supply a designated Windows host with .NET 10, an ordinary disposable NTFS
   test directory and permissions to enable per-directory case sensitivity.
   Use a test location with 8.3 name generation enabled for the short-alias row;
   lack of that facility is an unmet environment prerequisite, not a passing row.
   Do not change arbitrary volume settings on the user's behalf.
2. Complete/recheck T05's native GUID-volume/root/reparse/FAT pin/ABI and handle
   procedures, including the updated output access mask and file synchronization.
3. Set `$env:PSPMAN_REQUIRE_WINDOWS_COPY_TESTS = '1'` and run:

   ```powershell
   dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter FullyQualifiedName~WindowsCopyDestinationResourcesTests --logger trx
   ```

   All 20 actual rows must execute without skips. Record OS/build/architecture,
   filesystem/options, case/8.3 configuration, each row, retained output hashes,
   source equality and exclusive reopen/root rename after release. Expected
   native errors must not be generalized to conceal sharing/flush defects.
4. Run the six portable cases too, then build and full solution regression with
   the matching required archive runtime for that host. Collect coverage only
   after other processes finish; rebuild afterward. Include Windows paths in
   coverage and distinguish unexecuted code from proof.
5. On designated disposable PSP/FAT media, execute T10's actual allocation near
   capacity, long/short-name creation races, native case behavior, buffering,
   device loss/removal and flush/close matrix. Temporary NTFS roots do not replace
   physical PSP/FAT acceptance. Record any unsupported targets as open gates.

## Traceability and limits

| Requirements / acceptance | Prepared T14 evidence and limit |
| --- | --- |
| FR-3/FR-4; AC-2/AC-3 | Bound GUID-root admission, current evidence and allocation samples; Windows execution pending. |
| FR-5/FR-6/FR-18/FR-22; AC-4/AC-5/AC-17/AC-22 | Native alias/case claims, short creation guard, current consent, disjoint writers and structural/link outcomes; prepared native rows unexecuted. |
| FR-9/FR-10/FR-12; AC-7/AC-8/AC-10 | Managed callback lifetime passes; native wait/cancel/partial/flush/close release remains unexecuted. |
| FR-21; AC-21 | Prepared independent Windows source handles and unchanged bytes; actual engine/DVD/PSP integration is later work. |
| NFR-1/NFR-2/NFR-4 | Core boundary preserved and Linux regression checked; no Windows responsiveness, physical device or catalogue-mutation result is inferred. |

T15 requires its own plan/authorization and explicit handling of open native
dependencies. No concurrent production activation or formal SDD validation is
authorized by this enabling task.
