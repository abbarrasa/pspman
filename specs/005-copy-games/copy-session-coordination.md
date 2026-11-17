# T07 — Copy session coordination and safe removal

Specification 0.5, plan/tasks 0.2. This report covers the authorized T07
implementation and automated evidence. It is not whole-feature validation,
physical PSP acceptance or Windows native certification.

## Authorization and boundaries

The user requested T07. A task-specific plan was presented, including one
active operation, captured Spec 004 associations, relevant-change failures,
removal interception/reservation, tests, coverage and documentation. It
explicitly requested advancing with T06 and its earlier Windows dependencies
still open. The user's “Apruebo el plan” approved that scope and exception.
The branch `feature/005_T07_copy-session-coordination` started from clean local
dev at e0cc455. No commits, push, dependency additions or device-provider changes
were made. T08 requires its own scoped plan and approval before implementation.

## Implementation

[GameCopySessionCoordinator](../../src/PSPMan.Core/Transfers/GameCopySessionCoordinator.cs)
observes the caller-owned DeviceSessionService and executes the existing
GameCopyService. The caller supplies the selected game and host path syntax;
application composition and the copy screen remain T08 work. The coordinator
must be disposed before its caller-owned device session.

- Start resolves the current source group/volume and per-game PSP through
  existing association rules, requires fresh ready device/volume identity and
  exactly one usable PSP volume, and validates roots using the supplied syntax.
  Missing selection, unavailable remembered choices and multiple PSPs without a
  choice fail before archive access. No replacement is chosen implicitly.
- A serialized transition gate owns one active slot and device-removal
  reservations. No queue or concurrent transfer is introduced. Input prompts
  and file release retain the slot. No I/O, UI callback or await runs inside
  this gate; immediate token cancellation runs after releasing it.
- Immutable captured game/source/PSP evidence is checked on session changes,
  before engine entry and before coordinator terminal publication. Relevant
  selection, identity, generation, root, role/capability and availability
  changes fail the operation. Unrelated global revisions, device labels,
  other source-group choices and other devices do not invalidate it. A newly
  connected additional PSP does not redirect or invalidate a captured PSP.
- Engine terminal snapshots are withheld until coordinator arbitration after
  engine resource release. Selection/removal failures retain Failed status
  rather than appearing as user cancellation; close errors remain failures.
  Late terminal cancellation cannot reverse a retired operation. Stop flags
  are recorded under the gate before callbacks run, preventing completion from
  winning merely because a cancellation callback was delayed.
- Explicit cancel/disposal drains active work. Disposal cancels immediately
  outside the gate and awaits completion/reservations without disposing the
  device session. Reentrant disposal from preparation prevents source access.
  Failed presentation consumers cannot strand the active slot; retained-output
  warnings remain in failed results, and an authoritative Snapshot is available.

[IDeviceRemovalInterceptor](../../src/PSPMan.Core/Devices/IDeviceRemovalInterceptor.cs)
is a narrow awaited command boundary. The coordinator reserves the requested
device, fails/stops any matching copy, waits for closed files, validates the same
observed identity/generation/volume topology against the latest session and
forwards that same device to RemoveDeviceAsync. Unrelated revisions can advance
while it waits. Replacement devices, changed topology or stale requests are
rejected. Cancelling a removal request does not release the reservation before
copy files close, and does not forward a cancelled request.

The reservation remains while the session accepts removal, even after the copy
slot becomes free. Session acceptance publishes Removing before returning;
existing session/provider code then owns native completion, errors and retries.
Unrelated device removals preserve existing behavior and do not stop the copy.

[DevicesViewModel](../../src/PSPMan.Desktop/ViewModels/DevicesViewModel.cs) accepts
an optional interceptor; its existing guarded removal command awaits it.
Absent interception, removal and mount retry follow their existing paths.
Disposal cancels pending intercepted commands; rejection is shown through
DeviceActionError. No view/code-behind, automatic wiring, native removal policy,
mount rules or persistence behavior changed in T07.

## Test-first evidence

The initial focused Core build failed only because the coordinator did not
exist. Its first implementation passed 21 cases. New tests then reproduced
missing invalidation when a PSP lost its destination capability or gained a
second usable volume; both timed out instead of stopping. The checks were
added and those cases passed.

The initial Desktop focused build failed because the optional removal
constructor parameter did not exist. Implementing the awaited hook made the
new tests and ten existing command tests pass.

Later RED cases reproduced a missing retained-output warning after a throwing
progress consumer and source opening after disposal from the first preparing
snapshot. Failure warnings and immediate out-of-gate cancellation were corrected
before final verification. The preparation/disposal test then passes without
source access. Historical RED results are not passing evidence.

### Traceability

| Requirements | Automated evidence |
| --- | --- |
| FR-2, FR-3, NFR-1 | No game/source/PSP, multiple PSPs/volumes, remembered unavailable PSP without fallback; all three catalogue media; captured Linux and Windows-shaped roots. Windows-shaped fake evidence is not native Windows execution. |
| FR-9, FR-10, FR-11 | Single active operation through input/stop; cancel/dispose draining; explicit failure; retained prefix and warnings; observer failure recovery; final-result stability. |
| FR-16 | Game/source/PSP changes; source loss, identity/generation/root/role/volume changes and observer failure; unrelated device/source-group/label revisions ignored. |
| FR-19 | Indeterminate input retains the slot and already written bytes; selection/removal cancels pending input and rejects a late response without retrying writes. |
| FR-3, FR-16, NFR-4 | Removal spy observes closed files before the same device request; pending-close and pre-acceptance reservations reject a new copy; same-path replacement rejected; unrelated revisions use fresh evidence; native veto stays visible. |

The 41 Core cases use the real copy service, device session and coordinator with
controlled archive/file contracts and a synthetic provider. Three new Desktop
cases verify awaited interception, visible rejection and disposal cancellation;
all 13 device-command cases pass, including the existing unintercepted path.
They are boundary/command tests, not the future copy screen or real USB tests.

## Commands and coverage

Projects use xUnit v2 and Microsoft.NET.Test.Sdk/VSTest. Execute sequentially;
never instrument shared assemblies while another test/application uses them.

```sh
dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --filter FullyQualifiedName~GameCopySessionCoordinatorTests --collect:"XPlat Code Coverage" --results-directory <core-scratch>
dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --filter FullyQualifiedName~DeviceOperationCommandsTests --collect:"XPlat Code Coverage" --results-directory <desktop-scratch>
dotnet build PSPMan.slnx
dotnet test PSPMan.slnx
```

Instrumented focused runs: **41 Core passed**, **13 Desktop command cases
passed**. Coverage calculated from Cobertura source-file line unions and per-line
branch counts across generated async classes:

| Owning file | Lines | Branch outcomes |
| --- | --- | --- |
| GameCopySessionCoordinator | 239/244 (97.95%) | 200/245 (81.63%) |
| DevicesViewModel.Operations | 63/63 (100%) | 44/47 (93.62%) |

No production exclusion or numeric threshold was introduced. Coordinator gaps
include last-instant stop/observation arbitration after engine completion,
invalid-root rejection and the drain-error fallback. Remaining branch outcomes
include defensive invalid/stale combinations and close-error priority; command
gaps include existing disposed/refresh guard alternatives. Other device UI and
native providers are outside these focused percentages. Existing regression and
pending native validation remain necessary.

Final `dotnet build PSPMan.slnx`: **passed, zero warnings/errors**.
Final `dotnet test PSPMan.slnx`: **1788 passed, zero failures, 26 existing Windows
discovery skips** (Core 734, Infraestructure 739/26 skips, Desktop 315). The skips
remain the four device cases, 21 T05 discovery entries and one T06 theory; T07
adds no native Windows certification claim. Final editor diagnostics and
whitespace checks report no errors.

### Isolated graphical startup and close

Linux/X11 startup passed using `dotnet run --project
src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build --no-restore` with empty temporary
XDG_DATA_HOME/config/cache directories and a private dbus-run-session. Before
launch, the packaged DBusAddress.System property was verified by reflection to
respect DBUS_SYSTEM_BUS_ADDRESS; the private bus had no activatable UDisks2.
DBUS_SYSTEM_BUS_ADDRESS was then set to that private session address, preventing
real device discovery/mounting. The temporary catalogue file was checked.

The newly launched child process's visible PSPMan window was identified by its
executable path, PID and title, observed at **1200 × 650**, activated and closed
normally using Alt+F4. Application exit was **0**, not timeout termination; no
unhandled application exception was logged. Private-session portal/FUSE and
shutdown D-Bus warnings occurred, as in the earlier isolated T44 environment.
This checks startup compatibility of the optional constructor parameter and
normal existing lifetime cleanup. It does not compose a copy coordinator or
exercise the future copy screen, real media, mounting, Polkit or Windows.

To reproduce, designate fresh temporary XDG data/config/cache directories,
create and verify the private bus and its lack of UDisks2 activation, set the
system-bus override before launching the command, verify the owned window and
temporary catalogue, close that window normally, and record the process exit.
Never substitute ordinary startup against arbitrary connected media.

## Remaining native and feature acceptance

T06's four Windows engine rows, T01/T03/T05 native Windows gates, Spec 004
hardware/authorization prerequisites and T10 physical PSP checks remain mandatory
and unexecuted here. This dependency exception does not complete or waive them.
See [pending validation](./pending-validation.md) and [T06 evidence](./copy-engine.md).

A real Linux/Windows PSP must eventually demonstrate physical selection/loss,
blocked/released files, requested safe removal and native veto/privilege behavior
with designated source/destination media, including while FR-19 input is pending.
Record actual device identity, filesystem, process/architecture, retained bytes
and removal result. Synthetic provider requests and a private D-Bus startup are
not substitutes. The copy screen, startup composition and confirmed shutdown
flows belong to T08/T09; this task does not claim those acceptance criteria.
