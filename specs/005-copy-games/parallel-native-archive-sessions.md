# T12 — Parallel native archive sessions

Specification 0.6; plan/tasks 0.3. Status: **Linux checks passed;
mandatory Windows gate open, T12 incomplete**.

## Authorization and scope

After reviewing the presented task-specific plan and its explicit T01/T03
Windows dependency exception, the user authorized T12 with “Apruebo el plan con
la excepción”. Work started from clean local dev `ebd1cc7` on
`feature/005_T12_parallel-native-archive-sessions`. The exception permits available
Linux work; it does not complete T01/T03 or waive Windows/physical acceptance.

Only tests and evidence are added. The existing production reader/bridge already
passed the corrected overlap tests, so no speculative synchronization, engine,
ABI, package, Core, UI, database or device-provider change was made. No PSP,
removable source, native mounting, provisioning, commit or push was performed.
The ordinary application was not launched for this archive-only verification.

## Actual concurrency evidence

[GameArchiveConcurrencyTests](../../tests/PSPMan.Infraestructure.Tests/GameArchiveConcurrencyTests.cs)
uses the production SevenZipGameArchiveReader and its packaged native runtime.
Each session owns independent real read-only FileStreams and source positions;
outputs are controlled test streams backed by memory, not PSP files. Atomic
source-handle/read counters and per-target open/complete counters are checked.

Both native extraction invocations are held inside their managed output-write
callbacks after accepting data. Each barrier records entry, remains pending
until explicit release/cancellation and contributes to a shared active/peak
counter. Two entered barriers, two incomplete extraction tasks and peak 2 prove
overlap; starting two Tasks alone would not. A separate explicitly serialized
native control demonstrates peak 1 and that the second barrier cannot enter
before the first is released. Deadlines are hang guards, not performance claims.
The collection runs apart from other integration classes because it deliberately
blocks native workers and forces process-wide GC; concurrency within each case
still uses two real production native sessions. Assertion/timeout cleanup also
bounds Open/Inspect attempts and releases the held peer before draining them.

Fourteen paired cases cover shared ZIP/ZIP64/AES/LZMA, RAR4/RAR5/solid,
encrypted headers and multipart/split ZIP including selected non-first parts,
plus a ZIP/RAR pair. Complete payload hashes match the independent existing
fixture manifest's three expected nonempty outputs. Multipart sources record
multiple part names. Input hashes remain unchanged. Another case uses identically
named temporary archives with different byte patterns/lengths and checks exact
output/progress independence, preventing identical corpus payloads from hiding
cross-session stream/target confusion.

Five stop/fault cases exercise cancel, Dispose, write failure, close failure and
noncooperative callback disposal while a peer native invocation stays alive.
Partial output is retained and checked against the healthy session's exact
prefix. One module/session is destroyed while the other callback remains held;
forced GC exercises pinned delegate/runtime lifetime before the peer resumes.
For noncooperative I/O, disposal must remain incomplete with source/output still
owned, while the peer completes/disposes; only releasing the blocked callback
allows cancellation/native return and destruction of its session. Cleanup first
releases all peer barriers, so an assertion failure does not leave another owner
blocked by reverse disposal order.

Eight isolated error cases run while a healthy encrypted peer is held in a real
native callback: ambiguous wrong password, damaged encrypted input, wrong
header password, definite wrong password, missing part, late definite CRC,
empty archive and source I/O. Wrong-password cases explicitly reopen with the
correct fixture password and succeed while the peer remains pending. Requests
and password state belong to separate sessions. A further large stored-ZIP case
injects a later source read failure after the first native output write; a
nonempty retained prefix is checked while the other decoder continues normally.

All failures close target streams and source handles after native return.
These tests establish callback/session independence, not the later service/UI
queue, PSP confinement, destination capacity, per-file overwrite arbitration,
real device removal or application close. Earlier scoped service/UI FR-19
retry/retention evidence remains separate.

## Test-first observations

The tests were added before any potential production correction. Initial
execution passed 26/28 cases. Two failing assertions assumed a definite cause
for an ambiguous encrypted-header error and assumed a wrong password always
produced nonempty output. Existing approved behavior permits the ambiguous
result and zero written bytes. Expectations were corrected, a definite-password
case added, then the native read-callback case added. The corrected set passes
30/30. The first full regression then timed out in an existing private D-Bus
startup test. Cross-class scheduling interference was considered, not proven;
the new callback/GC collection was isolated. The subsequent combined native and
D-Bus run passed all 34 cases, and the final full regression passed as recorded
below. The existing D-Bus test was not changed or disabled.
This is verification of existing implementation, not a missing-behavior
RED/production fix, and no known rejection is counted as feature support.

## Verification and coverage

Host: Linux x86_64, .NET SDK 10.0.404, xUnit 2/VSTest with existing collector.
Only `linux-x64` runtime assets are staged; no closed deployment architecture
list is inferred. ABI 1, pinned 7-Zip 26.06/version 2606 and all 17 manifest file
hashes (engine, bridge, full pinned source, build sources and notices) were
independently rechecked. Each production session also validates runtime assets
and actual ABI/source-version exports. No installed tool or managed fallback is
used. Manifest consistency is not independent authentication of arbitrary DLLs.

Focused command, actual result: 30 passed, zero failed/skipped:

```sh
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter FullyQualifiedName~GameArchiveConcurrencyTests
```

`dotnet build PSPMan.slnx` passed with zero warnings/errors. The final
`PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx --logger trx` passed:
Core 761, Infraestructure 769 and Desktop 377, total **1907 passed**, with 26
existing Windows discovery entries skipped and zero failures. Required native
archive cases did not skip. The combined native/D-Bus filter was
`FullyQualifiedName~GameArchiveConcurrencyTests|FullyQualifiedName~LinuxTmdsUDisksTransportIntegrationTests`.

Sequential focused coverage passed 30/30 using the existing collector:

```sh
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --no-build --filter FullyQualifiedName~GameArchiveConcurrencyTests --collect:"XPlat Code Coverage"
```

| Owning managed source | Covered lines | Covered branches |
| --- | --- | --- |
| SevenZipNativeSession | 312/325 (96.00%) | 115/163 (70.55%) |
| SevenZipGameArchiveReader | 9/9 (100%) | No branch points |
| SevenZipNativeMethods | 12/13 (92.31%) | No branch points |
| ArchiveRuntimeResolver | 47/54 (87.04%) | 36/60 (60.00%) |

These are focused T12 managed source figures, not full-suite or C++ coverage.
The suite deliberately uses valid extraction indices, known bounded metadata,
readable/seekable fixed sources, writable controlled targets and intact runtime
packages. Uncovered session lines include invalid indices/metadata overflow,
loader failures, changed/missing sources, skip/invalid target contracts, unsafe
part names and password-required mapping. Runtime tampering/missing assets and
other guarded failures belong to earlier adapter/runtime regression cases; no
numeric coverage target or Windows/native C++ execution is inferred. Existing
branch alternatives also exceed the two-owner overlap scope. The solution was
rebuilt after instrumentation: zero warnings/errors.

Local framework-dependent Release publication passed using
`dotnet publish src/PSPMan.Desktop/PSPMan.Desktop.csproj -c Release -r linux-x64 --self-contained false -o <isolated-publish-directory>`.
The published manifest's ABI 1/version 2606/RID/source pin and all 17 independent
SHA-256 asset checks passed, including the full pinned source archive, bridge
build sources and notices. The original staged-runtime audit passed the same
checks. No production/native source or package format changed in T12.

After the post-coverage rebuild, all **30 focused cases passed again**, with
zero failures/skips, loading that local publication by setting
`PSPMAN_TEST_PUBLISH_ROOT` to its directory and retaining
`PSPMAN_REQUIRE_NATIVE_TESTS=1`. The test invocation used `--no-build`, the same
class filter and a TRX logger. Each real session additionally checked native ABI
and pinned source-version exports. This verifies Linux publication consumption;
it does not execute the Windows package or establish support for untested RIDs.

Final diff whitespace checks and editor diagnostics passed. No UI changed, so
graphical startup was not repeated. Windows execution and physical acceptance
remain unexecuted mandatory checks, not passes.

## Traceability

| Requirements / acceptance | Actual T12 evidence and boundary |
| --- | --- |
| FR-5/FR-14/FR-21; AC-4/AC-13/AC-21 | Actual shared-source packaged native sessions overlap, complete ZIP/RAR/solid/multipart payload hashes, independent positions/outputs; no optical-drive or PSP proof. |
| FR-9/FR-10; AC-7/AC-8 | One native owner cancels/fails/closes while the peer remains inside native work; exact retained prefixes and released callbacks/handles. No device-removal claim. |
| FR-13/FR-15/FR-19/FR-24; AC-12/AC-14/AC-19/AC-24 | Separate encrypted sessions, explicit reopen retry, ambiguous versus definite faults, missing/empty/CRC/source-I/O errors without affecting the healthy encrypted peer. UI prompt routing remains later work. |
| NFR-1/NFR-2/NFR-4 | Actual Linux runtime overlap, controlled pending I/O/GC and unchanged source hashes. Windows is unexecuted; no desktop responsiveness or catalogue mutation path is exercised by this archive-only suite. |

## Mandatory Windows/runtime restart procedure

No Windows endpoint/toolchain is established on this Linux host. The absence of
cmd.exe/PowerShell/VM tooling in PATH is an availability observation, not a claim
that a future environment cannot be supplied. No provisioning was authorized.
T12 stays unchecked until the equivalent real Windows cases pass, together with
its earlier prerequisite gates in [pending validation](./pending-validation.md).

On a designated Windows host with .NET 10 and a matching C++ toolchain:

1. Follow the [native build/package instructions](../../native/PSPMan.ArchiveBridge/README.md)
   and T01/T03 procedures to stage the exact pinned engine/bridge for the target
   architecture. Verify source SHA-256, ABI/version, all assets/notices and actual
   Windows native API/harness/reader results; do not reuse Linux binaries.
2. Set `PSPMAN_REQUIRE_NATIVE_TESTS=1`. Run the focused command above natively
   (PowerShell sets `$env:PSPMAN_REQUIRE_NATIVE_TESTS = '1'`). No PSP is needed
   for this archive/callback gate. Preserve actual test rows/results, OS/build,
   CPU/RID and fixture/runtime hashes. Missing assets must fail, never skip into
   an apparent pass. Repeat the serial control and all noncooperative/GC cases.
3. Build and run the full solution regression; collect owning infrastructure
   coverage sequentially after all test processes finish. Rebuild afterward.
   Record skipped native cases as open prerequisites, not successes.
4. Publish Desktop locally for the matching Windows RID with required assets;
   set `PSPMAN_TEST_PUBLISH_ROOT` to that directory and rerun the same focused
   tests. Verify package hashes/source/notices and actual native exports.
5. Check actual Windows source/output handle and module lifetime, including
   exclusive reopen on isolated fixture copies after all dependent sessions
   return/close; distinguish a peer legitimately holding the shared source.
   Leave T12 open for missing/failing rows or unsupported published targets.

T13 needs its own plan/authorization. Formal SDD review and whole-feature
acceptance are not authorized by this partial native gate.
