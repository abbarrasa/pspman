# T05 — Confined Windows copy filesystem

Status: implementation prepared; mandatory native Windows gate open.
Specification 0.5; plan/tasks 0.2. User authorization: “Continúa con T05”.
Branch: feature/005_T05_windows-copy-filesystem, from clean local dev f2198c4.
No commits or push. T02/T04 dependencies are complete; T01/T03 Windows gates
remain open. No requirements or task completion conditions have been waived.

## Implementation

The shared filesystem facade dispatches to WindowsCopyFileAccess on Windows,
preserving the existing Linux implementation. No Core contracts, device
providers, catalogue, UI, composition, package dependencies or archive runtime
packaging changed. Copy orchestration and user prompts remain later tasks.

Windows root sessions require the captured provider volume's canonical GUID,
device/volume identities, generation, ready state and observed root. The current
mount mapping must resolve to that exact GUID. Actual access starts from a held
GUID volume-root descriptor, then traverses selected subdirectories one component
at a time. This does not redirect to another device through a reused drive letter.
Observed folder mount points require a mount-point reparse tag naming exactly the
selected GUID volume; ordinary junctions and symbolic links are not accepted as
that authorization. All entry reparses below the selected root are rejected.

NtCreateFile opens one leaf relative to a verified parent descriptor. Parent
directory handles withhold write/delete sharing and remain pinned; metadata
inspection of existing files allows external changes while consent is pending.
Native per-directory case flags control object-name lookup rather than an
OS-derived string comparer. Managed name rules reject traversal, DOS reserved
names, streams, malformed Unicode and incompatible Windows names before calls.

File identity uses volume GUID/serial and 128-bit identifiers where supported;
unsupported legacy filesystems use documented volume-serial/64-bit file IDs.
ReFS is never authorized through its non-unique legacy 64-bit IDs. Native change
evidence and a bounded content hash supplement identity, including FAT's coarse
timestamps. Operation-local pin tokens change when a retained FAT identity moves
and its old directory-entry ID is reused. Consent never relies on size alone.

New outputs use FILE_CREATE exclusively. Existing-file opens use FILE_OPEN
without truncation, deny concurrent write/delete sharing, and compare current
path, held identity, change evidence and bytes before explicit end-of-file
mutation. A changed target returns TargetChanged; the adapter does not prompt.
Unsafe/link traversal yields skips; structural/name errors fail. Existing hard
links are skipped to avoid modifying another name outside ISO.

Capacity comes from NtQueryVolumeInformationFile on the held root, with checked
caller-available allocation geometry. Unknown/incomplete results fail preflight;
opening/inspection never creates ISO. Source parts are read-only seekable leaf
streams in the selected archive directory. The source token is independent of
the reader's combined inspection fingerprint. Sessions reuse the bounded stream
ownership/cancellation wrapper and release outputs, metadata pins and parents
on disposal. No destination deletion, restoration or full temporary extraction
is implemented. Native blocking I/O retains OS cancellation limitations.

Relevant primary API contracts:
[relative NtCreateFile](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile),
[file identities and FAT timestamp/ID limits](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information),
[128-bit identities](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info),
[handle-based capacity](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-zwqueryvolumeinformationfile),
[caller-available geometry](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_fs_full_size_information).
These contracts informed implementation; documentation is not execution proof.

## Executed verification

Host: Linux 6.12.116+deb13-amd64 x86_64; .NET SDK 10.0.404; xUnit/VSTest.
Reassessment found no cmd.exe/PowerShell, Wine or QEMU/libvirt/VirtualBox runner.
No accessible Windows endpoint or credentials have been supplied. No environment
was installed/provisioned, and no arbitrary devices were mounted or copied to.

- Initial 27 portable cases failed with NotImplementedException.
- Final portable cases: 31 passed. They verify names, traversal, malformed UTF-16,
  overflow/unknown capacity geometry, current-process ABI layout sizes, native
  open plans, status classification and selected-volume mount-tag parsing.
- One intermediate InlineData Unicode case failed because xUnit serialized an
  unpaired surrogate as replacement characters. The case now constructs its
  malformed string inside a Fact; this was a test-input correction.
- Native Windows discovery: 21 skipped entries on Linux, including one four-row
  archive theory. These represent 24 prepared native cases on Windows, not passes.
- Requiring Windows with PSPMAN_REQUIRE_WINDOWS_COPY_TESTS=1 on Linux caused an
  expected failing run (15 filtered discovery error cases at that revision),
  rather than silently passing/skipping the mandatory gate. No Windows API was
  executed by that deliberate platform-guard check.
- Final solution build passed with zero warnings/errors. Full regression passed
  1681 tests (648 Core / 721 Infrastructure / 312 Desktop), with 25 Windows skips:
  four existing native device cases and 21 T05 discovery entries. No failures or
  IPC timeout occurred in this run. Editor and whitespace checks pass.

### Scoped coverage, with native gaps explicit

Sequential Coverlet JSON/Cobertura collection passed the same 31 portable tests,
with 21 native Windows discovery skips. Counts below include generated method
branches from JSON; line hits are merged by file/line. No exclusions or numeric
acceptance threshold were introduced.

| New production file | Covered lines | Covered branches | Meaning |
| --- | --- | --- | --- |
| [WindowsCopyPathRules](../../src/PSPMan.Infraestructure/Transfers/WindowsCopyPathRules.cs) | 37/38 (97.37%) | 90/94 (95.74%) | Portable validation/geometry/tag parsing |
| [WindowsCopyNative](../../src/PSPMan.Infraestructure/Transfers/WindowsCopyNative.cs) | 17/130 (13.08%) | 40/124 (32.26%) | Only portable open plans/status predicates; no P/Invoke executed |
| [WindowsCopyFileAccess](../../src/PSPMan.Infraestructure/Transfers/WindowsCopyFileAccess.cs) | 0/240 | 0/176 | Native Windows root/session behavior unexecuted |
| [WindowsCopyDestination](../../src/PSPMan.Infraestructure/Transfers/WindowsCopyDestination.cs) | 0/109 | 0/76 | Native Windows destination behavior unexecuted |
| [WindowsArchiveSource](../../src/PSPMan.Infraestructure/Transfers/WindowsArchiveSource.cs) | 0/65 | 0/30 | Native Windows source behavior unexecuted |

The small remaining portable gaps are defensive alternative invalid-name/tag
branches. All native filesystem/API branches lack execution evidence until a
Windows runner is available. This report does not use high policy coverage to
claim native or feature coverage; collect actual owning-component coverage after
the Windows gate and again in T10.

No graphical startup was attempted: UI is unchanged and ordinary startup would
enable device discovery/mounting. No native Windows or physical PSP acceptance
is claimed by compilation, portable models, layout checks or Linux regression.

## Prepared native cases and restart

The WindowsCopy test group contains real Windows calls on dedicated temporary
roots, with synthetic inventory observations explicitly separated from hardware:

- Actual capacity/no preflight writes, nested/empty/Unicode output, duplicates,
  content-hash consent, exclusive-create collisions and target replacement.
- Case aliases and an explicitly case-sensitive NTFS directory, DOS/name errors,
  file/directory conflicts, hard links, junctions and file symlinks, late reparse
  insertion and source confinement/missing parts.
- Native sharing barriers preventing parent/file rename before truncation;
  captured GUID mismatch and media-generation changes stop mutation.
- Injected space/open/write/close errors and pending-write cancellation retain
  bytes and close owned handles. Disposal releases directory rename barriers.
- Four actual native-reader integrations: ZIP, solid RAR5, encrypted multipart
  RAR from a non-first part and split ZIP. Compare expected 100926-byte payload
  hashes, unchanged source fixture hashes and released file/parent handles.
  Archived executable payloads are hashed only, never executed.

Run on a Windows 10/11 NTFS runner with .NET 10, permitted case-sensitive
directories, and Developer Mode or suitable symlink privilege. Stage the matching
T03 native archive package (T01 build tools/source prerequisites still apply).

```powershell
$env:PSPMAN_REQUIRE_WINDOWS_COPY_TESTS = '1'
$env:PSPMAN_REQUIRE_NATIVE_TESTS = '1'
dotnet build PSPMan.slnx
dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter 'FullyQualifiedName~WindowsCopy' --logger 'trx;LogFileName=t05-native.trx'
dotnet test PSPMan.slnx --logger 'trx;LogFileName=t05-windows-regression.trx'
```

The focused group must execute 31 portable and 24 native cases on Windows, with
no T05 native skips. Missing privileges, NTFS case support, runtime assets or API
failures are unresolved prerequisites/failures, never fake-adapter acceptance.
Collect owning-file JSON/Cobertura coverage sequentially afterwards. Repeat
layout/API/runtime checks for supported Windows process architectures; Linux x64
layout sizes cannot certify Windows x86/arm64 calling behavior.

Actual mounted-folder/GUID roots, FAT legacy identifiers and same-time changes,
replaced drive mappings/volumes, native read-only/disk-full/flush errors and real
PSP/media removal require designated disposable volumes/hardware. See the exact
[pending validation](./pending-validation.md). No system-volume mount alias or
write outside temporary fixture roots is authorized merely by these procedures.
T05 remains open until its native completion checks pass. T06 is not started or
authorized, and formal SDD validation/code review is still a later phase.
