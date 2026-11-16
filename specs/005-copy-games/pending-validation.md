# Spec 005 — Pending native validation

Specification 0.5, plan/tasks 0.2. Unexecuted checks are mandatory and never count
as passing evidence. See [native T01 evidence](./native-archive-compatibility.md)
and [Spec 004 dependencies](../004-device-mounting/pending-validation.md).

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
