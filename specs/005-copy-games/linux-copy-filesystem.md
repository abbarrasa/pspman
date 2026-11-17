# T04 — Confined Linux copy filesystem

Status: T04 complete for its approved native Linux temporary-root scope.
Specification 0.5; plan/tasks 0.2. User authorization: “Continúa con T04”.
Started from clean local dev at bfa3fb2 on
feature/005_T04_linux-copy-filesystem. No commits or push.

## Implemented behavior and boundaries

FileGameCopyFileSystem accepts a caller-owned fresh inventory callback and
creates operation-local source/destination sessions. It does not discover,
mount or remove devices. Before opening roots and before data I/O/mutation,
it compares captured device/volume keys, identities, media generation, ready
state and root against the observation. Native statx mount/device/inode evidence
binds a held root descriptor; reopening a different directory at the same root
fails. Provider labels and unrelated inventory revisions do not authorize or
invalidate access. A cached root string alone is insufficient authorization.

LinuxCopyFileAccess walks full prefixes below the pinned root through openat2,
rejecting symlinks, magic links, traversal and nested mount crossings. Native
name lookup determines case equality; no host-derived case comparer is used.
Root opening and inspection never create ISO. Actual free bytes come from
fstatvfs on that captured root. Creating directories/output is a separate
explicit call after the future service's capacity check.

Source Location retains its medium-rooted interpretation; archive parts are
read-only, seekable leaf names in the selected archive directory. Source
evidence is captured before inspection, remains distinct from the archive
reader fingerprint, and detects changes to the selected file. Reads also
revalidate open-file identity and size/change evidence. Seek only changes a
pinned descriptor offset; data I/O performs validation, avoiding an asynchronous
provider wait inside Stream.Seek's uncancellable synchronous contract.

Absent outputs use exclusive creation. Existing files require matching
parent/file identity, length, native change evidence and a bounded SHA-256
content hash. Content hashing avoids relying solely on coarse filesystem
timestamps, at the cost of additional reads of existing destination files.
Both the held file and current path are checked before explicit ftruncate.
Changed targets return TargetChanged for renewed consent; the adapter does not
prompt or implement the T06 retry loop. Existing destination hard links are
also skipped to avoid modifying another name outside ISO.

Native lookup rejects incompatible names and file/directory conflicts. Unsafe
or link paths yield skips. The Core directory contract now returns an optional
skip reason, so a link found at mutation time is not silently counted as a
successful empty directory. This is a technical contract refinement for the
already approved FR-12 behavior, not a new product requirement.

Streams validate before data I/O, serialize I/O against disposal, close even on
flush/close failure and preserve the original error. Sessions own their streams,
root and consent pins and release all of them on disposal. Cancellation/failure
never deletes destination output or restores overwritten contents. Pins prevent
inode reuse from forging an earlier consent identity. No whole-game buffer or
full temporary extraction is introduced; hashes use 64 KiB buffers.

The native interop uses fixed-width statx/openat2 layouts rather than libc's
architecture-dependent struct stat. It currently dispatches Linux x64/arm64;
only x64 was executed. Kernels/filesystems must support openat2, statx mount IDs
and the required identity/change fields; unsupported systems fail closed.
See the upstream [openat2 UAPI](https://github.com/torvalds/linux/blob/master/include/uapi/linux/openat2.h)
and [statx UAPI](https://github.com/torvalds/linux/blob/master/include/uapi/linux/stat.h).
Windows dispatch belongs to T05. No production UI/composition, coordinator,
copy service, queue, persistence or device-provider behavior changed.

## Verification and scope

Host: Linux 6.12.116+deb13-amd64, x86_64, .NET SDK 10.0.404, xUnit/VSTest.
All writes use unique temporary test roots and licensed archive fixtures;
archived executable payloads are hashed, never executed.

- Initial 12 cases failed with NotImplementedException from the absent adapter.
- Final focused set: 48 passed, no skips, including instrumented coverage.
- Real Linux calls exercise nested/empty directories, read-only source parts,
  missing input, native case lookup, Unicode, invalid/unsafe names, symlinks,
  hard links, sibling-prefix confinement and an actual proc mount crossing.
- Deterministic mutation hooks replace a target before open/truncate, insert
  a file during exclusive create and swap parent/ancestor directories for links.
  No test writes outside its selected ISO root; foreign files remain unchanged.
- Fresh observation mismatch and real mount-root-directory replacement stop I/O
  without redirecting writes. Simulated space/open/write/close failures and a
  pending-write cancellation retain the expected bytes.
- Four native-reader integrations cover ZIP, solid RAR5, encrypted multipart RAR
  selected from a non-first part and split ZIP. Output hashes match T01's three
  payloads (100926 bytes); every source fixture hash remains unchanged.
- /proc/self/fd checks confirm root/source/output/consent descriptors close;
  repeated inspection does not grow consent pins. Session disposal also closes
  caller-unreleased streams. No physical device or native mount was simulated
  as a passing hardware test.
- Build: zero warnings/errors. Full regression: 1650 passed, four existing
  Windows skips (648 Core / 690 Infrastructure / 312 Desktop). Editor and
  added-line whitespace checks pass.

The initial full regression had 1649 passes and one existing IPC test failure:
WindowsOperationIpcTests.HostClosesOnLateIdentityChangesAndResponderFailures,
responder-wrong-id, received OperationCanceledException instead of
InvalidDataException with its 50 ms limit. The unchanged isolated IPC suite
passed 45/45; the unchanged full rerun passed 1650/1650 runnable cases. The first
failure is retained as timing-sensitive regression evidence, not silently
reclassified as a pass. No IPC implementation/test change was made.

Reproduce from the repository root:

```sh
dotnet build PSPMan.slnx
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter 'FullyQualifiedName~GameCopyFileSystemTests|FullyQualifiedName~LinuxCopyFileAccessTests|FullyQualifiedName~LinuxCopySourceTests|FullyQualifiedName~LinuxArchiveFileSystemIntegrationTests' --collect:'XPlat Code Coverage'
```

Stage the matched T03 native runtime first; the require flag prevents native
archive checks silently skipping. The four integrations use a test extraction
target, not the as-yet-unimplemented production service or copy UI.

## Scoped coverage

The final 48-case run also passed when collecting both Coverlet JSON and
Cobertura sequentially after regression, with no other tests/app using the
instrumented binaries. Scope is the six new production filesystem files only,
not the whole Infrastructure assembly or the completed copy feature.
Line counts merge hits by file/line; branch counts include every JSON method's
branch points, including generated async/lambda types. JSON is used because
Cobertura class line details omit some generated-type branch points.

| File | Lines covered/valid | Branches covered/valid |
| --- | --- | --- |
| [FileGameCopyFileSystem](../../src/PSPMan.Infraestructure/Transfers/FileGameCopyFileSystem.cs) | 20/22 (90.91%) | 1/2 (50.00%) |
| [LinuxCopyNative](../../src/PSPMan.Infraestructure/Transfers/LinuxCopyNative.cs) | 40/45 (88.89%) | 25/38 (65.79%) |
| [LinuxCopyFileAccess](../../src/PSPMan.Infraestructure/Transfers/LinuxCopyFileAccess.cs) | 169/179 (94.41%) | 106/138 (76.81%) |
| [LinuxCopyDestination](../../src/PSPMan.Infraestructure/Transfers/LinuxCopyDestination.cs) | 119/127 (93.70%) | 91/108 (84.26%) |
| [LinuxArchiveSource](../../src/PSPMan.Infraestructure/Transfers/LinuxArchiveSource.cs) | 58/68 (85.29%) | 20/30 (66.67%) |
| [CopyAccessStream](../../src/PSPMan.Infraestructure/Transfers/CopyAccessStream.cs) | 60/68 (88.24%) | 30/36 (83.33%) |
| **Total** | **466/509 (91.55%)** | **273/352 (77.56%)** |

Uncovered paths include unsupported architecture/kernel/statx capabilities,
rare native permission/read/free-space errors, replacement while collecting
evidence, and alternative managed stream/cleanup exception branches. Injected
space/open/write/close faults cover failure propagation and retained bytes but
do not establish actual hardware error behavior. No exclusions or numeric
acceptance threshold were added. T10 retains full owning-component coverage and
physical validation. To reproduce JSON as well, configure the XPlat collector's
Format as `json,cobertura` in runsettings and use the same focused filter.

## Remaining validation

Native temporary-root tests prove the executed filesystem/API behavior with
synthetic device observations. They do not prove a real PSP/FAT volume's space,
case/name semantics, physical disconnect, UDisks/mount replacement, permissions,
disk-full behavior or safe removal. Those mandatory cases remain in
[pending validation](./pending-validation.md) and T10. No graphical startup was
performed: this increment does not change UI, and ordinary startup would enable
discovery/mounting against arbitrary hardware. T01/T03 Windows gates are still
open. T05 and formal SDD validation/code review are not started.
