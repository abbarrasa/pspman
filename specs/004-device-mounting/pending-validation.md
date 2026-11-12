# Device mounting — Pending validation runbook

Status: **Pending execution**
Recorded: 2026-11-12
Scope: existing acceptance gaps under approved specification 0.3 and plan 0.1; no new product requirements or authorization to execute operations.

See [tasks](./tasks.md) for dependencies, [specification](./spec.md) for acceptance criteria and [recognition matrix](./manual-validation.md) for model-specific evidence.

## Baseline and evidence rules

The last recorded T38 regression passed 1328 tests and explicitly skipped four Windows-native checks on Linux. Headless UI, fake platform boundaries and isolated fake UDisks transport results do not certify Windows kernel behavior, physical hardware, native authorization or all PSP models. No new tests were executed when writing this runbook.

All checkboxes below remain open until an actual execution record supports closure. A skipped test, zero observed removable volumes or an unavailable model is a remaining gap, not a pass. Virtual-device evidence and physical-device evidence must be recorded separately. T36's suite implementation is complete, but its Windows-native execution remains pending. T49–T53 retain their existing completion gates.

T44 wires the visible device workflow into the application: ordinary startup now begins discovery and may automatically mount supported attached devices. Do not launch it against arbitrary hardware for a smoke test. Execute native end-to-end procedures only after their dependent tasks, including T48, are complete and execution is authorized with the safety gates below. An isolated private D-Bus with no UDisks service can check Linux startup/close without device operations, but cannot certify real hardware or Polkit. This document does not start T45 or the formal validation/code-review phase.

## Architecture and tools

| Environment | Architecture and tools | Valid evidence and limits |
| --- | --- | --- |
| Local Windows VM on Linux | QEMU/KVM, optionally managed through virt-manager; usable virtualization access; Windows installation media; guest Windows and .NET 10 SDK; guest desktop access; disposable virtual system/data disks and snapshots | Real Windows API execution against virtual hardware, interactive apphost/UAC checks. Virtual hardware does not certify a real PSP, reader or optical drive. |
| Optional Docker-managed VM | dockur/windows with its documented QEMU/KVM prerequisites and explicitly reviewed device/network permissions | An alternative way to manage a full Windows VM, not a Windows kernel simulated by a Linux container. It offers no stronger evidence than that VM. Installation/configuration is future work. |
| Windows CI | GitHub Actions Windows-hosted runner, checkout of the exact revision, .NET 10 setup, test reports | Automated Windows regression and native read-only checks on hosted virtual hardware. Not evidence for local USB hardware, interactive UAC or physical ejection. Workflow creation is pending and requires separate authorization. |
| Windows VM with hardware | The local VM above plus explicitly selected USB passthrough and disposable USB/Memory Stick/PSP hardware | Real device enumeration and storage observations through the guest Windows driver stack. Record passthrough limitations; removal can affect guest attachment without proving physical power-off. Confirm real removal claims separately. |
| Native Windows machine | Windows desktop, .NET 10 SDK, isolated test catalog, selected disposable devices; optional Device Manager/Disk Management and process/token inspection tools | Reference environment for physical-driver, removal/veto, reconnect and UAC behavior when VM results are ambiguous or differ. |
| Native Linux desktop | .NET 10 SDK, system D-Bus, UDisks2, a functioning Polkit agent, USB metadata tools such as lsusb, isolated test catalog and disposable devices | Real Linux service/authorization/hardware evidence. An isolated fake bus does not replace this environment. |

Wine is a compatibility layer, not the Microsoft Windows implementation; any exploratory Wine results must not close native Windows security/device acceptance. Conventional Linux containers do not supply a Windows kernel.

Primary environment references: [Windows containers versus VMs](https://learn.microsoft.com/en-us/virtualization/windowscontainers/about/), [QEMU USB devices and host passthrough](https://www.qemu.org/docs/master/system/devices/usb.html), [dockur/windows prerequisites](https://github.com/dockur/windows), [GitHub-hosted runners](https://docs.github.com/en/actions/concepts/runners/github-hosted-runners), [Wine architecture](https://www.winehq.org/about/). Recheck current prerequisites before provisioning; these links are not evidence that any environment was installed or tested.

## Safety and preparation — required before execution

1. Select and record the exact repository revision, guest/host OS builds, architecture, SDK and apphost publishing mode. Do not assume a floating runner label identifies a reproducible Windows release.
2. Prepare isolated application data and a test catalog in the guest/test account. Do not point the application at the user's real collection. Use snapshots to recover VM state; snapshots do not protect data on passed-through physical media.
3. Begin without physical passthrough. Keep the host system disk, collection disks and unrelated USB devices outside the guest. Do not grant broad raw-disk/USB access merely to simplify setup.
4. For later mutating checks, obtain explicit approval for each selected disposable device and operation. Back up its contents beforehand, outside PSPMan. Do not format, repartition, repair, copy games, force removal or kill a process to satisfy a test.
5. Ensure a passed-through medium is not concurrently mounted/used by the Linux host. After safe host release, attach only the selected device using the VM manager. Recheck ownership after guest ejection or reconnection before touching it on the host.
6. Use an ordinary Windows account/process for normal UI execution; do not run the entire UI or test suite elevated to bypass failures. Native UAC owns credentials and its secure desktop. Do not capture credentials or sensitive prompts in reports.
7. Keep remote desktop/web consoles local or access-controlled; do not expose a VM console or remote test runner publicly. Review any required installation privileges/device permissions separately.

## W1 — Windows read-only native integration (T36, NFR-1)

- [ ] Execute the existing suite on real Windows, initially in the VM without physical passthrough.

Steps inside Windows, from the repository root:

```powershell
dotnet --info
dotnet restore PSPMan.slnx
dotnet build PSPMan.slnx --no-restore
dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --no-restore --filter FullyQualifiedName~WindowsNativeReadOnlyIntegrationTests --logger "console;verbosity=detailed"
dotnet test PSPMan.slnx --no-build --no-restore --logger trx
```

Record four executed native cases separately from the two platform-policy cases: storage enumeration/structured errors and pre-cancellation; disk/CD-ROM/volume interface registration/idempotent disposal; observer subscription-before-inventory and lease disposal; cleanup after injected inventory failure. A global enumeration failure must fail, not become a successful empty scan. Keep legitimate per-device access/no-media errors visible.

Record device/volume counts, native error operation/codes and acquired volume-watch count. Zero volume watches leaves target-volume registration coverage pending. Registration lease-disposal assertions are not an independent OS handle-leak measurement. Repeat with selected supported removable hardware later to close that gap. These tests do not mount, remove or launch UAC.

## W2 — Native security, UAC and launch modes (T31–T35, T51)

- [ ] Validate real pipe ACL/token/peer behavior and interactive UAC.
- [ ] Validate dedicated development and published Windows apphost launches.

1. Run development through the generated Windows executable, not by treating generic dotnet.exe as a trusted helper identity. Build a published apphost for the chosen Windows architecture as a separate test artifact. Record exact executable paths/build configuration privately, redacting personal path components from shared evidence.
2. Check normal launch opens the unelevated UI. Invalid helper arguments must exit without opening UI/catalog services. Do not manually fabricate a valid privileged device-operation command.
3. Use the completed application workflow on a disposable eligible target only when its ordinary operation genuinely requires authorization. If that condition cannot safely be arranged, record the case as pending; do not modify global policy to manufacture it.
4. Observe native approval, denial and cancellation separately. Verify one fixed helper, unchanged UI privilege, independently observed operation outcome, no success on denial/cancellation and no automatic prompt loop. Verify explicit retry and suppression reset on confirmed disconnect/media change, not root/label-only changes.
5. Existing fake-process/pipe tests do not prove Windows ACLs, native token access or actual peer PIDs. Prepare a separately authorized native integration harness for wrong-process/creation-time/session/elevation peers, unauthorized/remote connection rejection, disconnects, timeouts and cleanup. Keep it on an isolated VM and avoid real device mutation. Until that harness exists and runs, these security cases remain pending.
6. If cross-account elevation is available, record its token-query behavior. A fail-closed rejection is not evidence that the cross-account workflow succeeded; report the limitation instead of relaxing authentication.

## W3 — Windows discovery, roots and hardware recognition (T51, AC-1/AC-8)

- [ ] Observe startup inventory, insertion, empty readers and multi-volume roots.
- [ ] Verify PSP model/storage presentations against the recognition matrix.

1. After UI integration, start with already mounted test devices; record existing roots and verify reuse without duplicate mounting. Repeat with insertion after startup.
2. Include optical media/empty drive, USB storage and card reader with/without media. Verify per-volume availability, missing/duplicate labels, supported multi-volume media and meaningful native failure feedback. Do not create partitions during validation.
3. Exercise an eligible unmounted volume only on an explicitly approved target. Verify fresh drive-letter selection and actual observed roots; never overwrite mappings, activate protected disks or alter automount policy. Unsafe-to-reproduce letter races remain simulated/pending native evidence.
4. Attach each available PSP family/storage presentation. Record sanitized device/interface descriptors and storage association; confirm destination/source separation. Labels/directories or Sony VID alone must not establish PSP identity. Unknown descriptors must remain an explicit recognition gap.
5. Mark each unavailable family/firmware/storage presentation pending in the existing matrix. A virtual USB disk or generic PSP-looking label cannot close a physical PSP row.

## W4 — Windows removal and session behavior (T52, AC-2–AC-5/AC-8)

- [ ] Observe whole-device removal, veto/busy failures and optical-media-only eject.
- [ ] Observe source/destination choices, disconnect/reconnect and restart.

1. Select a disposable multi-volume source; request safe removal and confirm the scope is that physical device, not a sibling/shared hub. Confirm actual remaining volume states after both success and failure.
2. For a busy-volume case, use a dedicated test process holding an existing harmless test file open on the disposable medium. Expect a native error/veto, no forced removal and no false success. Close the handle normally before explicit retry; do not terminate the process as part of PSPMan behavior.
3. For optical media, eject the medium and confirm the drive stays enumerated and empty. USB passthrough detachment is not proof that a tray physically opened; use native Windows hardware if needed.
4. After confirmed successful removal, verify no automatic remount until disconnect/reinsert. Reconnect with changed roots where safely possible and verify associations recover only with sufficient identity evidence; a different card or reused drive letter must not inherit an old selection.
5. With multiple source devices, choose independent optical and shared USB/Memory Card preferences, then a source volume. Show the full stored Location joined to the chosen root without probing game contents, changing catalog values or automatically falling back when a chosen medium disappears.
6. With two recognized PSPs, choose different destinations for two games. Disconnect/reconnect one, then restart: unavailable choices must not silently redirect, session choices must clear on restart, and automatic mounting must be enabled again.
7. During pending operations/UAC, verify unrelated catalog viewing and UI input remain responsive. Record exact expected/observed feedback and any VM-specific deviation.

## L1 — Remaining native Linux acceptance (T49–T50)

- [ ] Observe real UDisks2/Polkit and hardware behavior, not just a fake bus.

1. Verify system D-Bus, UDisks2 and a graphical Polkit agent are available. Run the completed UI as an ordinary user with isolated catalog data. Record unavailable services honestly.
2. Execute W3's startup/insertion, mounted-root reuse, reader/optical and model-matrix scenarios on Linux; record actual Linux roots and USB ancestry evidence.
3. On selected disposable media, observe Polkit approval/denial/cancellation where authorization is genuinely needed, plus no automatic prompt loop and explicit retry. Do not weaken Polkit rules or run the whole app as root.
4. Execute W4's removal, busy-volume, optical eject, reconnect, root/identity, independent source preferences, two-PSP and restart scenarios using Linux native services. Confirm actual remaining state after failures and no remount after a successful explicit removal until reinsertion.

## Evidence reconciliation (T48, T53 and later validation)

- [ ] Measure owning-suite line/branch coverage and explain significant native/hardware gaps.
- [ ] Reconcile every acceptance criterion and every PSP matrix row with current evidence.
- [ ] Request separate authorization for formal validation/code review; do not declare the feature implemented merely because CI is green.

For each case, record: case ID; date; revision; OS/SDK/architecture; native versus VM/CI environment; relevant device family/firmware/storage presentation; prerequisites; exact command or UI steps; expected and actual results; counts/errors; sanitized report/screenshot references; pass/fail/pending verdict; remaining limitations. Do not store unique serials, credentials, tokens or personal data. Keep failures as failures until investigated; do not silently substitute a mock or skip.

An optional Windows CI workflow is a future change, not part of this documentation update. It should restore/build/test on a pinned Windows runner image and .NET SDK, retain sanitized test reports, distinguish native skips from executed tests and avoid unattended interactive UAC or physical-device mutation. Re-run the read-only filter with detailed output to retain the coverage limitations described in W1.
