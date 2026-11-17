# Spec 005 — Pending native validation

Specification 0.5, plan/tasks 0.2. Unexecuted checks are mandatory and never count
as passing evidence. See [native T01 evidence](./native-archive-compatibility.md)
and [Spec 004 dependencies](../004-device-mounting/pending-validation.md).

## T06 Windows engine integration — open

The user approved the scoped T06 plan and its explicit T03/T05 Windows
dependency exception. Core implementation and Linux verification do not waive
native or physical checks. Four native Windows service integration cases are
prepared, with zero executions in this Linux session; their theory appears as
one skipped discovery entry. Follow [T06 procedures](./copy-engine.md), staging
the matching T03 Windows engine/bridge first and enabling both required-native
environment guards. Execute all four archive rows, check output/source hashes
and handle release, then full build/regression. Complete T03/T05's still-open
checks as well. Record Windows OS/architecture/filesystem and actual results;
no fake or Linux test substitutes for these executions. T06 remains unchecked
and T07 has not started. Physical PSP/UI/removal/close flows remain T10/T07–T09.

## T05 Windows confined filesystem gate — open

The user authorized T05 with “Continúa con T05”. Implementation and portable
checks are prepared; native Windows execution remains mandatory. Reassessment
on 2026-11-17 still finds Linux x64 and no accessible Windows interpreter,
PowerShell or VM runner. No Windows host/credentials were supplied; no
provisioning or native/physical operation was performed.

Use a Windows 10/11 NTFS runner with .NET 10, allowed per-directory case-sensitive
flags and Developer Mode or suitable symlink privilege. Build/package the matched
T03 engine/bridge first. Run the [T05 commands and cases](./windows-copy-filesystem.md)
with both PSPMAN_REQUIRE_WINDOWS_COPY_TESTS=1 and PSPMAN_REQUIRE_NATIVE_TESTS=1.
The focused WindowsCopy group must execute all 31 portable and 24 native cases
without T05 native skips. Linux reports 21 skipped discovery entries for these
native tests because the four-row archive theory collapses to one skipped entry.
Portable marshalling/open-plan/name tests do not prove Windows API behavior.

Required native follow-up includes NtCreateFile directory-relative/no-follow
opens, parent/file sharing barriers, target replacement/consent, Unicode and
native case-sensitive lookup, hard links/junctions/symlinks, source confinement,
actual capacity, retained output on I/O/cancel/close faults and released handles.
Run actual ZIP/RAR/solid/encrypted multipart/split-ZIP integrations, verify source
and output hashes, then full build/regression and owning-file coverage. Repeat
ABI/API checks on supported Windows process architectures; Linux x64 layout
checks cannot certify Windows x86/arm64.

Use only designated disposable volumes for further mounted-folder/GUID alias,
FAT identifier reuse/coarse-time overwrite, changed drive mapping/media and
native read-only/full-volume/flush/removal checks. A scratch VHD or real test PSP
and dedicated source media must be explicitly prepared before mutation. Never
mount the system volume under a fixture path and write ISO to that real root.
Record the actual OS/architecture, filesystem, native API results, handles,
hashes and failures. Physical PSP/device-provider/removal/UI evidence still
belongs to T10 and Spec 004's native prerequisites. T05 remains unchecked until
its native completion gate passes; T06 proceeded only under the separately
approved dependency exception recorded above.

## T03 Windows production adapter and publication — open

The user authorized T03 after T02 while keeping Windows tests deferred. Linux
implementation and 48 production adapter tests, including published Linux x64
assets, pass; this does not complete T03's required Windows gate.

On the Windows environment described below, build the matched engine/bridge,
then run package-windows.ps1 with the official pinned source archive and matching
Platform. Require native tests with `PSPMAN_REQUIRE_NATIVE_TESTS=1`; run tests
filtered by `FullyQualifiedName~GameArchiveReader`, then full solution build/tests.
Exercise passwords, FR-19 reopen, solid replay, multipart, Unicode, cancellation,
callback disposal and source/output handle closure using the production adapter.
Publish Desktop with the matching win RID, verify the manifest's complete 17
assets/source/notices, and rerun the adapter matrix with
`PSPMAN_TEST_PUBLISH_ROOT` pointing to that publication. Confirm missing/mismatched
assets fail without PATH fallback and missing packages fail publication.
Record actual commands, OS/architecture, hashes and counts. Windows scripts have
not been executed here. Other CPU/libc targets also have no native/publish proof;
do not infer them from Linux x64. No PSP is needed for these archive-only tests.

## T04 Linux filesystem hardware follow-up — mandatory for T10

T04 executes native Linux filesystem calls with unique temporary roots and
synthetic device observations. Its proc mount-crossing check is an actual
read-only kernel boundary check; it is not PSP/mount-provider evidence.
The following remain unexecuted without a real PSP and dedicated media:

- Mount a designated single-volume PSP/FAT filesystem through the normal device
  provider. Capture actual device/volume identities, root and media generation;
  compare reported free bytes to the volume's observed available space.
- Use disposable source archives and destination files. Verify nested/empty
  directories, Unicode, names rejected by that actual filesystem, case-only
  collisions, per-file overwrite consent and coarse-time same-size modifications.
- Exercise insufficient capacity, actual disk-full/read-only/write/flush failures,
  disconnect and actual volume replacement at the same mount path. Confirm no
  redirection, source hashes unchanged, all handles closed and output retained.
- Run safe-removal and close/cancel flows after T06–T09 implement orchestration
  and UI. Confirm stream release before removal/exit and no destination cleanup.

These physical results must be recorded per the existing T10 matrix. Synthetic
root replacement and injected I/O failures do not pass them. Linux arm64 and
older-kernel/filesystem rejection paths also lack execution evidence; only Linux
x64 with openat2/statx mount-ID support was exercised in T04. No hardware or VM
provisioning is implied by this record. See [T04 evidence](./linux-copy-filesystem.md).

## T01 native Windows archive gate — open

- Blocker: this session provides Linux x64, with no accessible native Windows
  runner/VM. Linux native results and reference CLI output do not prove Windows.
- Environment reassessment after the user requested the remaining T01 tests:
  host inspection confirms Linux x64; no Windows command interpreter, PowerShell,
  Wine, QEMU, libvirt/VirtualBox command or GitHub CLI is available. A KVM device
  exists, but that alone provides neither a Windows guest nor a VM launcher.
  Available connected tools expose no Windows execution endpoint. SSH exists,
  but no Windows host/access target has been supplied. Windows execution remains
  blocked on identifying an accessible machine/VM/runner; no tests were rerun or
  reclassified as passing during this reassessment.
- Future environment: Windows with .NET 10 SDK, Visual Studio C++ build tools,
  Windows SDK/nmake, matching target developer shell, and dedicated local build
  and temporary fixture directories. No PSP is needed for this archive-only gate.
- Verify the official 7-Zip 26.06 source SHA-256 from the pinned lock file. Build
  full Format7zF and the bridge using [native instructions](../../native/PSPMan.ArchiveBridge/README.md).
  Record compiler, OS/CPU, engine/bridge hashes and copied licence notices.
- Set `PSPMAN_REQUIRE_NATIVE_TESTS=1`; run infrastructure tests filtered by
  `FullyQualifiedName~NativeArchiveBridge`. All fixture rows must execute, not
  silently skip. Run the native Unicode/concurrency harness with cl as described
  in the same instructions. Run solution build/full regression afterwards.
- Required cases: ZIP/ZIP64/encrypted deflate and LZMA, RAR4/RAR5/solid,
  header/data encryption, legacy/modern/encrypted multipart and ordered part
  callbacks, non-first selection normalization, metadata/size/hash identity,
  duplicate Unicode paths, links/skips, missing parts, empty/CRC corruption,
  password retry/input cancellation, ambiguous wrong-password/damaged ciphertext,
  partial writes, original I/O cause, cancellation during pending read, pinned
  delegates, closed handles, unchanged source bytes and ABI rejection.
- Record native handle release before reopening the source files exclusively on
  Windows; Linux simulated/test-local streams cannot substitute for this check.
- T01 remains open until these pass. The user subsequently explicitly deferred
  these tests and authorized proceeding to T02 (pure contracts/rules). That
  execution exception does not mark Windows results passed or waive final
  acceptance; it does not authorize T03, provider changes or copying to user media.

## Runtime packaging and architecture checks — T03/T10 pending

The current Desktop project declares no RuntimeIdentifiers or publish profiles.
Inventory actual deployment targets before packaging; do not silently restrict
Linux/Windows support to this session's x64 CPU. Build/test matched engine/bridge
for every supported published target, verify trusted application-local resolution,
source/notices distribution and reject missing/mismatched assets. T01's source
version export is not an engine-binary authentication mechanism.

## Physical copy and UI matrix — T10 pending

No copy service/UI exists at this point, and T01 performs no PSP I/O. All physical
copy tests remain unexecuted: Linux/Windows mounting and identity, PSP absence and
multiple choice, source media, actual capacity, nested output, passwords/parts,
overwrite/renewed consent/skips, progress, retention on cancellation/corruption/I/O
failure, selection changes, removal after handle release and confirmed closure.
Prepare explicitly designated disposable media and known payload hashes before
those tests. Cross-reference outstanding device authorization/hardware cases in
Spec 004; do not infer their completion from archive tests. Graphical startup that
would discover/mount arbitrary hardware is not part of this T01 gate.
