# Spec 005 — Pending native validation

Current specification 0.6, plan/tasks 0.3. Earlier sections/reports retain their
historical single-copy scope. Unexecuted checks are mandatory and never count
as passing evidence. See [native T01 evidence](./native-archive-compatibility.md)
and [Spec 004 dependencies](../004-device-mounting/pending-validation.md).

## Current T10 continuation — approved partial evidence, mandatory gates open

On 2026-11-18 the user approved the scoped plan and continuation with
“Apruebo el plan y la continuación parcial con las pruebas pendientes”. Work
starts from clean local `dev` at `77d5f11` on
`feature/005_T10_concurrent-copy-verification`. This renews the explicit
unavailable-environment deferral for available T10 verification; no Windows,
physical, architecture or Spec 004 prerequisite is completed or waived.

The [current verification/coverage report](./coverage.md) records automated,
native package/publication and controlled graphical evidence. The
[requirement/acceptance index and P01–P26 matrix](./manual-validation.md) now
cover all 24 FR/AC and four NFR, preserving earlier physical procedure IDs while
reconciling obsolete one-slot/selection-change expectations with revision 0.6.
Every P row remains BLOCKED/unexecuted on both physical platforms. Passing
temporary-root native and controlled-inventory tests does not satisfy them.

Available continuation results: root builds before/after coverage pass without
warnings/errors; full regression passes 2037 with zero failures and 37 Windows
discovery skips. Sequential owning line/branch coverage is Core 96.64%/89.78%,
Infraestructure 71.38%/60.32% and Desktop 94.70%/89.73%; all 15 assembly copies
restore their hashes after each collector. Staged/published linux-x64 assets
match all 17 hashes; published native checks pass 109 with two Windows skips.
Missing runtime publication is rejected as expected. Controlled concurrent X11
closure and isolated production startup/resize/normal close exit 0. These
passing available checks do not change any blocker in the following table.

| Outstanding scope | Blocker retained | Environment and exact restart path |
| --- | --- | --- |
| Windows archive bridge/reader and parallel sessions: T01/T03/T12 | No designated native Windows execution host/package; no Windows result | Establish the selected Windows CPU/RID and .NET 10 desktop/toolchain; build/package the pinned bridge/engine and verify every source/notice/hash. Set `PSPMAN_REQUIRE_NATIVE_TESTS=1`; run the bridge/reader suites and all [T12 parallel-session cases](./parallel-native-archive-sessions.md), first against staged assets and then the selected publication root. Retain ABI/version, actual native overlap, source hashes and session-release results. |
| Windows filesystem/resources/engine: T05/T06/T14/T15 | Same missing native host; Linux and portable rules cannot execute these APIs | After archive prerequisites, set both `PSPMAN_REQUIRE_WINDOWS_COPY_TESTS=1` and `PSPMAN_REQUIRE_NATIVE_TESTS=1`. Execute [Windows shared resources](./windows-destination-resources.md) and [engine-resource matrix](./copy-engine-resources.md), including all 13 engine scenarios, confinement/alias/case/sharing/space races, failure/cancel/retry and release. Then run full Windows regression/coverage and publish checks. A required-Windows run on Linux must fail, not close this gate. |
| Linux real PSP/source/reader matrix: P01–P26, Spec 004 T49/T50 | No designated disposable PSP/source media or established real-service authorization setup for this continuation | Use a native Linux desktop, system D-Bus/UDisks2/Polkit agent and explicitly selected backed-up media; complete Spec 004 discovery/mount/authorization/removal prerequisites. Execute each P row with per-operation captures, logical catalogue state and source/output hashes. Two PSPs and multiple actual mounted readers/discs are required for their respective cases. |
| Windows real PSP/source/reader matrix: P01–P26, Spec 004 T51/T52/T53 | No designated Windows environment or physical targets | Complete the relevant [Spec 004 native sequence](../004-device-mounting/pending-validation.md#current-blockers-and-restart-order), including ordinary-account apphost/UAC and actual device forwarding. Execute P01–P26 on the designated PSP/filesystems/readers, documenting VM/passthrough limits if used; hosted CI alone cannot prove physical removal or USB presentation. |
| OS desktop shutdown and real native authorization/removal delays: P15/P16/P25/P26 | Actual Linux/Windows session/logout and device-delay cases not executed | On the designated test desktop/media, exercise normal OS logout and confirmed aggregate closure with active, waiting and independent-input owners. Verify no late admission, files/native callbacks/resources released before exit/removal, retained bytes and unrelated continuation. A forced kill is not graceful-shutdown evidence. |
| Deployment architecture inventory and packages | Only linux-x64/glibc assets are staged/executed; the project has no declared publish profiles/RuntimeIdentifiers | Identify actual Linux/Windows deployment CPU/libc targets, package matching native assets and run native/published tests on each actual target. Retain source/notices/hashes/ABI and platform evidence; do not infer a narrower support promise from the single available package. |

The current NativeConcurrentShutdownTests fixture is deliberately Linux-only.
A passing Windows regression that skips it cannot establish Windows aggregate
native-callback closure. P26 needs actual Windows release-order evidence with
appropriate observation/delay instrumentation or an approved Windows-native
controlled counterpart; any additional test-code work requires its own scoped
authorization rather than being inferred from this documentary continuation.

No Windows/VM/CI provisioning, media formatting or arbitrary device discovery,
mount, overwrite or ejection is authorized by this documentary continuation.
Use the existing reports' setup commands and explicit target-selection
procedures before future hardware execution. No formal SDD review/acceptance is
authorized. T10 remains unchecked until all mandatory evidence passes.
Next: report the completed available consolidation and stop. Resume outstanding
native/physical checks when their designated environments/media are established,
with the applicable scoped execution plan; do not infer permission to move to
formal SDD validation from this partial continuation.

## T19 production activation — scoped integration complete, final T10 open

“Apruebo el plan” approved the presented T19 scope. The
[aggregate lifetime report](./concurrent-copy-lifetime.md) records production
explicit concurrent submission, Core-owned admission closure and aggregate
decline/accept/drain. Thirteen new cases include four actual packaged Linux
7-Zip/native-filesystem callback cases, covering separate writes/shared-file
waits under confirmed and programmatic close. Native release/descriptor and
source/output assertions pass. Final regression passes 2037 tests with zero
failures and 37 Windows discovery skips; sequential Desktop coverage and
controlled X11/isolated private-bus startup/closure pass.

This does not execute Windows equivalents, physical PSP recognition/mounting,
multiple real readers/discs/PSPs, filesystem behavior on actual destination
media, Polkit/UAC, OS logout or forced termination. All earlier native gates
and final T10 remain mandatory. Final T10 must extend the native/physical matrix
to the activated production flow, including independent inputs, shared-device
loss/removal, resource waits and aggregate close while native callbacks or
startup/removal work remain outstanding. Reuse the existing environment/media
preparation and source/output-hash procedures; simulated inventory and temporary
native filesystem roots are not hardware acceptance.

T19's next step was final T10 planning; that later partial-continuation approval
and its retained prerequisites are recorded above. No provisioning or formal
SDD validation/review is authorized here.

## T18 submissions — local choices, inherited native gates unchanged

“apruebo el plan y la implementación” approved T18's displayed scope. The
[submission report](./copy-submission-choices.md) records separate catalogue
multi-selection, local reader/volume/PSP choices, indexed partial admission and
explicit resubmission. Controlled composition exercises additions through the
real coordinator; at T18 completion production remained single-copy pending T19.
T19's activation and unchanged gates are recorded above. No shared associations, catalogue records or native policy are
modified by preparing/submitting games. Existing Windows/physical rows remain
mandatory; multiple actual readers/discs/PSPs must be covered by final T10.
T19 was subsequently authorized and completed as recorded above. Formal SDD
validation remains unauthorized.

## T17 presentation — independent rows, inherited native gates unchanged

“Apruebo el plan y su implementación” approved the displayed T17 plan. The
[presentation report](./concurrent-copy-presentation.md) records typed captured
rows, sequenced progress and private per-operation password/overwrite/FR-19
input. Controlled concurrent submission exercises the real Core coordinator;
production batch admission remains for T19 after T18's selection/choice work.
Available headless/graphical checks cannot establish physical PSP, optical,
Windows filesystem or removal/authorization acceptance. All earlier native and
physical rows remain mandatory; T10 must cover the final production flow.
T18's later approval and evidence are recorded above. No formal SDD validation
is authorized.

## T16 coordinator — scoped checks complete, inherited native gates unchanged

“Apruebo el plan y su implementación” approved the T16 scoped plan and explicit
exception to continue while T15's native Windows dependency remains open. The
[coordinator report](./multi-operation-coordinator.md) records the registry,
immutable batch capture, independent cancellation/revisions, shared-device stop
and removal reservations, and identity-bound close/drain APIs.

Controlled Core/awaited headless evidence verifies ownership and affected
single-row behavior; it is not actual native archive/device removal, optical
media, PSP/FAT, UAC or Windows filesystem evidence. T15 and all earlier native
prerequisites below remain mandatory and unchecked. T10 must exercise the final
concurrent production flow after T17–T19 activate it; no device/provider or
authorization policy is changed here. T17's later authorization and evidence are
recorded above; formal SDD validation remains unauthorized.

## T15 engine resources — integration prepared, native Windows gate open

“Apruebo el plan y autorizo a realizar T15” approved the presented scoped plan
and explicit unavailable-Windows dependency exception. The [engine resource
report](./copy-engine-resources.md) records mandatory shared composition,
actual-size reservations, per-entry waits/consent, retry settlement and release
before terminal results. Linux uses the actual packaged reader and confined
temporary-root filesystem, with injected scheduling/capacity/fault seams;
portable probes are not native acceptance.

Thirteen Windows engine-resource scenarios are prepared. Execute them on the
designated native host with both `PSPMAN_REQUIRE_WINDOWS_COPY_TESTS=1` and
`PSPMAN_REQUIRE_NATIVE_TESTS=1`, alongside existing native service integration,
full regression and T12/T14/T06's earlier Windows prerequisites. The required
Windows guard must reject a Linux-only attempt instead of counting skipped rows
as success. Follow the report's restart commands and retain native runtime,
filesystem, source-hash and post-terminal handle evidence.

T15 stays unchecked pending native Windows; T10's physical PSP/FAT/optical/device
matrix is unchanged. No public concurrent admission or aggregate close/removal
is enabled before T16–T19. T16's later scoped authorization and unchanged
dependency gates are recorded above; no SDD validation is authorized.

## T14 Windows resources — implementation prepared, native gate open

The user approved the presented T14 plan and explicit open-T05 dependency
exception with “Apruebo el plan y autorizo inciar la tarea”. This authorizes
available implementation/checks, not native completion or hardware provisioning.
[T14 protocol and restart procedure](./windows-destination-resources.md) records
the shared internal adapter, Windows GUID/file claims, exclusive creation,
file-handle synchronization and required native matrix. Six portable checks and
Linux resource regression are separate from Windows proof.

Twenty actual Windows rows are prepared (ten skipped discovery entries on
Linux). A designated Windows/.NET 10 host must run them with
`PSPMAN_REQUIRE_WINDOWS_COPY_TESTS=1`, including an ordinary NTFS test location,
permissions for per-directory case sensitivity and enabled 8.3 generation for
the short-alias row. Missing those facilities leaves that row open; do not alter
arbitrary volume settings. Recheck T05's native output access/share behavior and
flush/identity lifetime with the updated write mask. Native alias/creation races,
simultaneous writers/readers, free-space samples, buffer/partial/flush/close faults
and actual exclusive reopen/root rename must pass without skips.

T14 remains unchecked. No Windows endpoint is established; no native Windows
allocation, ABI, syscall, case/short-alias or handle outcome is claimed. T10's
designated PSP/FAT/device-loss matrix and earlier Windows gates stay mandatory.
T15's later scoped authorization and unchanged dependency gates are recorded
above; this earlier exception completes no Windows dependency.

## T13 Linux resources — physical filesystem follow-up remains in T10

The user approved the presented T13 scope with “apruebo el plan”. T13 depends on
completed scoped T11/T04; this is not a waiver of T12's Windows gate. The
[Linux resource report](./linux-destination-resources.md) separates actual native
temporary-root I/O from controlled capacity/fault observations and future engine
integration. Tests ran on Linux x64 tmpfs, not a PSP/FAT device or physical disk.

On designated disposable Linux PSP/FAT media, record kernel/filesystem/mount
options and actual free-space/allocation behavior before and after concurrent
buffered/partial/failed writes, retries, flush and close. Exercise exact-capacity
admission, unknown capacity, case/short-alias and new-name races, compatible
directory creation, unsafe/link/structural changes, wait cancellation and device
loss/removal with retained output and real handle release. Unsupported or
unexecuted filesystem/kernel/architecture cases stay pending; temporary-root
tests do not certify them. Do not create mounts, loop devices or operate arbitrary
hardware without the designated environment. These remain mandatory T10 gates.

T14's later scoped authorization and T15's engine integration are recorded above.
No concurrent production submission or aggregate close/removal behavior is
enabled by these resource tasks.

## T12 independent native sessions — mandatory Windows gate open

The user approved T12's presented plan and explicit T01/T03 Windows dependency
exception with “Apruebo el plan con la excepción”. Linux available checks may
proceed; T12 remains unchecked until mandatory Windows execution passes. No
provisioning, native device operation or waiver follows from this exception.

[T12 evidence and restart procedure](./parallel-native-archive-sessions.md)
records actual production-reader/native overlap, shared source/parts, separate
password/error/cancel ownership and native-return-before-disposal tests. These
use real fixture FileStreams and controlled memory outputs, not PSP writes.
The serial control must distinguish peak 1 from two entered native callbacks.
Run every case on the matching Windows engine/bridge with required-native guards,
then against its local published runtime; record hashes, ABI/version, native
source/stream/module release, GC and noncooperative callback outcomes. Finish
earlier T01/T03 Windows prerequisites separately; Linux success is no substitute.
The host remains Linux x86_64 with only linux-x64 runtime assets staged and no
established Windows endpoint/toolchain. No Windows or physical PSP result is
claimed. Subsequent T13 authorization is recorded above; it does not complete T12.

## T10 consolidation — authorized; mandatory gates still open

The user approved the presented T10 plan with “apruebo el plan”, explicitly
allowing available checks/documentation while earlier Windows dependencies stay
deferred. [Current regression/coverage and package evidence](./coverage.md) and
[requirement traceability, physical matrix and restart steps](./manual-validation.md)
record this work. T10 remains unchecked; documentation is not a replacement for
unexecuted Windows/PSP tests. No requirement, task completion condition or Spec
004 native gate is waived. Formal SDD validation/code review is not authorized.

The host is Linux x86_64; no Windows execution endpoint or designated disposable
PSP/source media has been established. Current PATH has no cmd.exe,
powershell.exe, pwsh, qemu-system-x86_64, VBoxManage or virsh. This is an
availability observation, not proof that the user cannot supply an environment.
The user was asked about available Windows/test media; no arbitrary connected
hardware may be inferred as authorized. No provisioning or physical operation
was performed. Only linux-x64 native assets are staged; other deployment targets
still need explicit inventory and matching native verification.

## T09 physical/native shutdown follow-up — mandatory for T10

T09's [confirmed-shutdown evidence](./confirmed-shutdown.md) covers awaited
headless lifetime tests and real Avalonia/X11 interaction with controlled I/O.
These are not real PSP, native archive cancellation or OS logout tests. On
designated Linux/Windows media, test decline/accept/repeated closure during
actual preparation, decode/write, password, overwrite and FR-19 decisions.
Confirm retained output hashes, prior-copy preservation and actual file/runtime
release before device disposal/removal and process exit. Exercise native
removal veto/privilege delays, unplugging and completion/close races; record OS,
architecture, packaged engine/bridge version and filesystem. Verify the normal
desktop shutdown request route separately from forced process termination;
graceful cleanup cannot be inferred for a killed process. Earlier Windows and
physical gates remain mandatory. T10's subsequent authorization is recorded above.

## T08 native copy-screen follow-up — mandatory for T10

The approved T08 presentation uses the existing native adapters in application
composition, but its 34 automated cases use controlled file/archive contracts and
synthetic providers. Follow [T08 evidence](./copy-screen-interactions.md) on
designated Linux/Windows media: select a real game/source/PSP, verify actual-byte
progress and responsiveness, masked password retry/cancel, definite corruption
versus “Incorrect password or damaged archive”, renewed per-file overwrite consent,
partial/all-skipped results and retained-output warnings. Confirm real hashes and
native release before removal, including selection changes during input/I/O.
Record OS, architecture, filesystem, native package version, device identities,
expected/actual hashes and actual outcomes. No graphical or fake-provider check
substitutes for these physical tests. Confirmed application closure still belongs
to T09, whose scoped implementation is now recorded above; earlier Windows and
T10 physical gates remain open.

## T07 native coordination follow-up — mandatory for T10

T07 proceeded under an explicitly approved task-specific dependency exception;
T06 and earlier Windows gates remain open. Its coordinator/device-command tests
use synthetic providers and controlled file contracts, not real PSP/native
removal evidence. Follow [T07 scope and procedures](./copy-session-coordination.md)
on designated Linux/Windows source media and a USB PSP after T08/T09 composition:
selection changes/loss during reads and password input, retained bytes,
replacement devices at the same mount path, copy-file release before the same
native removal request, visible native veto/privilege errors and cancellation.
Record actual identities, filesystem/architecture, hashes and file-release/removal
ordering. The earlier Windows and physical gates are neither completed nor waived.

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
and T07 proceeded only under its separately approved exception. Physical PSP/UI/
removal/close flows remain T10/T08–T09 with the T07 native follow-up above.

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

The copy service, UI and confirmed shutdown are now implemented through T09;
T01's archive-only evidence remains historical. All physical copy tests remain
unexecuted: Linux/Windows mounting and identity, PSP absence and
multiple choice, source media, actual capacity, nested output, passwords/parts,
overwrite/renewed consent/skips, progress, retention on cancellation/corruption/I/O
failure, selection changes, removal after handle release and confirmed closure.
Prepare explicitly designated disposable media and known payload hashes before
those tests. Cross-reference outstanding device authorization/hardware cases in
Spec 004; do not infer their completion from archive tests. Graphical startup
must not discover/mount arbitrary hardware. Follow P01–P18 in
[the manual matrix](./manual-validation.md) on explicitly designated media.
