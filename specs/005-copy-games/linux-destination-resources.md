# T13 — Shared Linux destination resources

Specification 0.6; approved plan/tasks 0.3. Status: **T13 scoped Linux work
complete**; Windows resources and physical acceptance remain separate gates.

## Authorization and scope

The user replied “apruebo el plan” to the presented T13 implementation plan.
Work started from clean local dev `1eb7c4d` on
`feature/005_T13_linux-destination-resources`. Dependencies are completed scoped
T11 and T04; native archive decoding is independently covered by T12, whose
Windows gate remains open. No Windows exception is needed or implied by T13.

This adds an opt-in resource owner and Linux adapter support. Existing production
single-copy composition, GameCopyService, coordinator, UI, archive bridge,
database, device detection/mounting and packages are unchanged. T15 will integrate
the resource protocol into engine calls; T19 will enable aggregate composition.
No removable media, PSP, provisioning, graphical startup, commit or push was used.

## Components and ownership

- [CopyDestinationResources](../../src/PSPMan.Infraestructure/Transfers/CopyDestinationResources.cs)
  implements the existing Core contracts using the immutable T11 ledger.
- [LinuxCopyDestination](../../src/PSPMan.Infraestructure/Transfers/LinuxCopyDestination.cs)
  exposes internal held-root identity, guarded native claim creation and settled
  capacity sampling. [BoundRoot](../../src/PSPMan.Infraestructure/Transfers/LinuxCopyFileAccess.cs)
  exposes its captured evidence without exporting handles through Core.
- [LinuxCopyNative](../../src/PSPMan.Infraestructure/Transfers/LinuxCopyNative.cs)
  adds syncfs for completed-allocation reconciliation; existing openat2/statx,
  no-follow traversal, conditional truncation and current observation checks
  remain final authority.
- [CopyDestinationResourcesTests](../../tests/PSPMan.Infraestructure.Tests/CopyDestinationResourcesTests.cs)
  uses actual Linux temporary files, descriptors and metadata with controlled
  provider observations, free-space samples and I/O failure/barrier seams.

One owner is intended per device session. The caller opens one confined native
destination per operation and passes it to `BindDestination`. The returned
IGameCopyDestination is the controlled integration boundary: reserve actual
size, acquire one entry, inspect/confirm/open through the binding, close output,
then release entry. Close the budget before disposing the caller-owned native
destination. Binding disposal retires its budget, not the caller's root handle.
Raw destinations remain unchanged for the existing single-copy engine; callers
must not mix raw mutations with the coordinated flow.

Bindings reject mismatched native/captured evidence. Active operation IDs cannot
be reused across volume states; different captured volumes cannot independently
promise the same held native root. Per-volume budgets use captured identity and
media generation, checked by native root identity rather than mount spelling.
Independent captured volumes have independent admission boundaries.

## Accounting protocol

Each volume has a short admission fence and a separate namespace guard. A chunk
acquires the fence only to enter its allocation epoch; it releases the fence
before data I/O. Entry/input waits and source decoding hold neither fence.
Completion takes only the state monitor, so an admission holding the fence can
always await already-started chunks without blocking their completion.

After draining chunks, admission flushes owned output buffers, synchronizes the
held filesystem and samples free bytes through the validated root. Only this
consistent sample clears completed unsettled charges before atomically applying
the Core reservation transition. This avoids both promising the same bytes twice
and rejecting a fitting request by charging already-allocated bytes twice.
No overwrite/deletion/skip credit is guessed. Unknown/insufficient space fails
admission; it does not wait for future space.

Bound streams account in at most 64 KiB chunks independently of UI progress.
Known partial failures record their accepted offset delta; unknown completion
charges the full requested chunk until a successful native reconciliation.
Retry replaces the full pending-attempt claim supplied by the engine, excluding
completed entries at that later integration boundary. Skip releases pending
bytes only. Failed/cancelled sampling preserves conservative charges. If final
sampling fails, the closed orphan claim remains until a later successful sample
can settle/release it. Written files are never removed or restored.

An output buffer failure discovered during another operation's admission is
attributed to its owning budget. Its pending I/O is cancelled and its stream
closed before native sampling; the owner observes the stored fault on further
resource use/final release. It is not propagated as an unrelated admission's
error. Actual capacity/volume failure can still reject that admission normally.
Final engine row/fault arbitration remains T15/T16 work.

Budget closure cancels its own queued entry and cooperative bound writes, drains
entry acquisition/output/native calls and all manual allocation epochs, then
settles and releases unused reservations. A noncompleted manual write lease
keeps closure pending; disposal cannot invent completion or reclaim its bytes.
Close failures remain visible after target exclusion is released.

## Native target protocol

The short namespace guard reuses confined root-to-leaf parent creation and
native lookup. Existing targets are registered by statx identity; hard links
and unsafe/link paths retain the existing skip rules. A missing file is created
exclusively **after actual-size admission**, and its inode registered before an
output stream is exposed. Native resolution of that creation handles equivalent
names without a host-wide case comparer. The guard is released before content
hashing, consent, writes or file lifetime.

The operation's own unchanged empty placeholder is reported as Missing, and its
actual evidence is used for conditional opening without an artificial overwrite
prompt. External changes lose that exemption and require current consent.
Cancellation/failure before content writing can therefore retain an empty file;
it follows the same no-cleanup policy as other accepted destination output.
Later operations see it as an ordinary existing file and require consent.

A conflicting operation waits cancellably, holding no other file claim. After
wake it revalidates and resolves the current native target before hashing or
consent. One operation owns/awaits one entry and cannot reopen an output while
its current output remains open. Compatible parent directories merge; structural
or incompatible-name failures never rename/delete the other operation's data.
Target replacement before output opening refreshes the claim and invalidates old
consent. During active I/O the old inode stays claimed through output close;
native current-path validation reports changes instead of redirecting writes.

## Verification and test-first evidence

Host: Linux x64, kernel 6.12.116+deb13-amd64, .NET SDK 10.0.404; temporary roots
were on tmpfs. These are actual native filesystem calls, not disk-durability or
physical FAT evidence. No native PSP/source media were touched.

The initial two tests failed compilation because CopyDestinationResources was
absent. Incremental behavior tests then reproduced and corrected three defects:
budget close did not cancel a pending entry wait; peer buffer failure escaped
into unrelated admission; an entry could reopen/truncate its still-owned output.
The final focused set passes **32/32**, including native output overlap, same-file
wait/cancel/reinspection, current consent after replacement, native case lookup,
concurrent parent creation, structural/name/link/unsafe outcomes, late parent
swaps, unchanged sibling/outside data and actual descriptor release.

Accounting tests cover samples before/after allocation, buffered native output,
known/unknown partial writes, unsuccessful close/sample, orphan settlement,
retry/skip, exact fits, overflow, cancelled admission fences, volume independence,
budget close during cooperative I/O and pending manual epochs. Controlled sample
values model capacity races; they are not measurements of real PSP free space.

```sh
dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter FullyQualifiedName~CopyDestinationResourcesTests --collect:"XPlat Code Coverage"
```

Focused managed coverage: owner **364/392 lines (92.86%)**, **158/193 branches
(81.87%)**; whole LinuxCopyDestination source **161/179 (89.94%)** and **65/86
(75.58%)**, including pre-existing single-copy paths. Owner gaps include defensive
binding/lifecycle misuse, rare race/failure alternatives, directory/stream facade
methods and disposed-output paths. Existing native-adapter regression covers
additional single-copy guards; this focused figure is not full-solution coverage,
Windows coverage or physical FAT evidence. No new numeric threshold is imposed.

Initial build passed with zero warnings/errors and the 31-case full regression
passed 1938 tests with 26 existing Windows skips. The final post-coverage
`dotnet build PSPMan.slnx` passed again with zero warnings/errors. Final
`PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx --logger trx` passed
**1939 tests**: Core 761, Infraestructure 801 and Desktop 377, with 26 existing
Windows discovery entries skipped and zero failures. All 32 new Linux resource
cases and the required Linux native archive cases executed without skips.
Editor diagnostics, diff whitespace and documentary link checks passed. No
package/ABI/publish change or UI startup was needed in this resource-only scope.

## Traceability and remaining gates

| Requirements / acceptance | T13 evidence and boundary |
| --- | --- |
| FR-4; AC-3 | Checked shared admission, actual pending bytes, consistent allocation samples, retry/skip/retained accounting; synthetic capacity values supplement real Linux streams. |
| FR-5/FR-6/FR-18/FR-22; AC-4/AC-5/AC-17/AC-22 | Native file identity, exclusive creation, current consent, file waits, distinct-file overlap, directory/name/link outcomes. Archive extraction and UI routing are separate tasks. |
| FR-9/FR-10/FR-12; AC-7/AC-8/AC-10 | Cancellable waits/chunks, drain-before-release, isolated peer faults, exact retained bytes, no deletion/restore. Aggregate application close is later work. |
| NFR-1/NFR-2/NFR-4 | Actual Linux openat2/statx/syncfs and disjoint I/O overlap; no UI/Windows/physical responsiveness claim, source/catalogue path absent and sibling bytes preserved. |

At the T13 baseline, the owner rejects non-Linux destination bindings. The later
[T14 adaptation](./windows-destination-resources.md) prepares the corresponding
Windows binding/protocol; its mandatory native execution remains open.
Earlier Windows gates remain open. T10 must execute on designated disposable
PSP/FAT media: actual allocation granularity, native case/short aliases and
missing-name races, concurrent free-space near capacity, shared directories,
unplug/removal and flush/close failures on real storage. Record kernel, filesystem,
mount options and CPU/OS; unexecuted filesystems/kernels/architectures are not
certified by temporary-root execution. No provisioning or hardware access was
inferred. See [pending validation](./pending-validation.md).

T14 needs its own scoped plan/authorization and explicit handling of its open
Windows prerequisite. Formal SDD validation/whole-feature acceptance is separate.
