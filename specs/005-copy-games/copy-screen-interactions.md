# T08 — Copy screen and interactions

Specification 0.5, plan/tasks 0.2. This is scoped implementation evidence, not
whole-feature acceptance, physical PSP proof or Windows native execution.

## Authorization and implementation

The user requested the next task and explicitly approved its presented T08 plan
with “Apruebo el plan”. Work started from clean local dev at abbf9d3 on
`feature/005_T08_copy-screen-interactions`. The existing Windows/physical gates
remain open; T08 depends on completed T07. No new dependency, provider policy,
catalogue mutation, commit or push is introduced. T09 is not started.

- [GameCopyViewModel](../../src/PSPMan.Desktop/ViewModels/GameCopyViewModel.cs)
  projects one active operation, actual-byte work, combined progress, cancellation
  and terminal messages. Version/terminal guards reject delayed progress after
  completion, disposal or another start. The active row remains busy through
  output closure. Failure and cancellation visibly warn that output remains and
  overwritten files are not restored.
- [CopyInputViewModel](../../src/PSPMan.Desktop/ViewModels/CopyInputViewModel.cs)
  represents one transient input lease. Its commands capture that lease, so a
  retired button cannot respond to a replacement prompt. Password fields are
  masked and cleared on response/cancellation/disposal; passwords are not placed
  in status, snapshots, notifications or persistent state. Each overwrite has
  Overwrite/Skip/Cancel; a changed target creates a fresh prompt. Ambiguous
  encrypted errors display “Incorrect password or damaged archive”, offer retry
  or cancellation, and retain the same active row.
- [GameCopyView](../../src/PSPMan.Desktop/Views/GameCopyView.axaml) uses compiled
  typed bindings, a single table row, keyboard-accessible controls, automation
  names and bounded vertical/horizontal scrolling. Its code-behind initializes
  only. [MainWindow](../../src/PSPMan.Desktop/Views/MainWindow.axaml) has an
  explicit catalogue Copy to PSP action and a bounded full-width copy panel.
  Existing game/source/PSP choices remain accessible at 900 × 450.
- [MainViewModel.Copy](../../src/PSPMan.Desktop/ViewModels/MainViewModel.Copy.cs)
  attaches optional caller-owned presentation without changing old constructors.
  Selected-game changes reach the coordinator. Copy stays an explicit action;
  missing game, source, PSP, unresolved multiple PSPs and startup errors are
  visible without automatic retry. Catalogue-only consumers keep their layout.
- [CopyStartup](../../src/PSPMan.Desktop/CopyStartup.cs) composes the existing
  filesystem/archive adapters, engine, coordinator and presentation using the
  caller-owned device session. Filesystem validation reads the latest observed
  session inventory; native pinned-root checks remain authoritative. Tests may
  inject file/archive contracts without changing native provider behavior.
- [ApplicationDeviceLifetime](../../src/PSPMan.Desktop/ApplicationDeviceLifetime.cs)
  attaches copy after provider construction, wires the existing removal hook,
  detaches it on disposal and drains the copy owner before disposing the device
  session. Device disposal still runs if copy disposal throws. This is minimal
  composition/lifetime support; **active-copy close confirmation and its races
  remain T09**. Current normal close stops the copy without confirmation.

### Acceptance before background execution

Integration exposed a race when the entire coordinator start was queued from
the view model: a selection change before the worker ran could substitute a
different game or report no selected game instead of failing the accepted copy.
A regression first reproduced this as “Select a game to copy.” after an immediate
selection change. The coordinator now captures/reserves synchronously in StartAsync
and queues only its existing engine runner on Task.Run. The view model calls
StartAsync directly. This small scheduling adjustment preserves existing Core
contracts/policy while fixing acceptance identity and keeping synchronous adapter
work off the UI thread. The regression now reports the selected-game change.

## Verification

Initial RED compilation failed because the copy view/model/input model did not
exist. The first four presentation cases then passed. Intermediate runs caught
a typed DataContext binding error; corrected tests also removed assumptions about
optional CSV pickers and matched the engine's existing capacity error text.
Those intermediate failures are historical, not passing evidence.

The final focused command passed **34 cases**, including:

- compiled view bindings, masked input and keyboard Enter retry;
- missing game, absent PSP and ambiguous PSP feedback before archive access;
- actual size differing from catalogue SizeBytes, combined progress and UI
  responsiveness during paused I/O;
- zero writes on insufficient/unknown capacity, empty archives or missing source;
- renewed consent, stale commands, partial/all-skipped qualified completion;
- password retry/cancel, definite corruption versus ambiguous encrypted errors,
  closed partial output before retry and renewed overwrite consent;
- game/source/PSP changes, user cancellation, retained bytes and file closure;
- immediate selection changes after submission, stale progress after a new
  operation, pending output closure, one active slot and no catalogue writes;
- optional catalogue-only consumers, initialization errors, application attachment,
  disposal and preservation of caller-owned session lifetime;
- minimum 900 × 450 layout and ViewLocator namespace resolution.

```sh
dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --filter FullyQualifiedName~GameCopy --no-restore --collect:"XPlat Code Coverage" --results-directory <isolated-results>
dotnet build PSPMan.slnx
dotnet test PSPMan.slnx
```

Coverage was collected sequentially, with no application/other test process using
the instrumented assemblies. Files include their nested state machines, merging
repeated source-line entries. No production exclusions were added.

| Owning file | Lines | Branches |
| --- | --- | --- |
| GameCopyViewModel | 162/169 (95.86%) | 87/98 (88.78%) |
| CopyInputViewModel | 19/19 (100%) | None reported |
| MainViewModel.Copy | 17/17 (100%) | 10/12 (83.33%) |
| CopyStartup | 16/19 (84.21%) | 9/10 (90%) |
| ApplicationDeviceLifetime, focused T08 flows only | 67/94 (71.28%) | 13/30 (43.33%) |

Uncovered presentation code is defensive early cancellation/unexpected startup
or disposal exceptions. CopyStartup's native observation callback is intentionally
not exercised with synthetic roots: it requires controlled native media. The
lifetime percentage is from the focused T08 filter, not a claim about all existing
lifetime tests or T09's unimplemented close confirmation. Native Windows dispatch
and physical behavior remain unverified.

Final `dotnet build PSPMan.slnx` passed with **zero warnings/errors**. Final
`dotnet test PSPMan.slnx` passed **1,822 tests**: Core 734, Infraestructure 739,
Desktop 349, with **26 existing Windows-only skipped discovery entries**. The
unchanged catalogue/device/boundary/lifetime suites passed alongside the 34 new
presentation cases. The full run was repeated after the acceptance-race fix and
the minimum-size heading adjustment; these are final-state results.

Linux/X11 smoke execution used `dotnet run --project
src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build --no-restore`, a temporary
XDG catalogue/config/cache and a private D-Bus. DBUS_SYSTEM_BUS_ADDRESS pointed
to that private bus, with no activatable UDisks2 service. The owned application
window was observed at 1200 × 650 and resized to 900 × 450. Clicking the visible
Copy to PSP action displayed the table and “Select a game to copy.”, with the
existing catalogue/editor/device panels bounded. The first image revealed a
clipped progress-column heading; TextWrapping was added and build/full regression
repeated. A second isolated graphical execution confirmed the entire heading
wraps visibly at 900 × 450 and the explicit missing-game feedback remains visible.
Normal Alt+F4 closure exited **0** in both executions, with no unhandled application
exception. Private portal/FUSE/keyring warnings are environment diagnostics,
not native-copy evidence. No physical media, actual archive transfer or active
copy close confirmation was exercised by this smoke check.

## Remaining native gates and next step

Follow [pending validation](./pending-validation.md). On designated Linux/Windows
media, verify packaged archive engine loading, real ZIP/RAR/password/multipart
copy, actual capacity, per-file consent, native names/case behavior, retained
partial bytes and device removal after file release. No real PSP or removable
media was used by the automated presentation tests. Real-device keyboard/screen
and native removal evidence are mandatory T10 work. T01/T03/T05/T06 Windows gates
remain open. Present T09's scoped plan and obtain approval before implementing
confirmed shutdown; this task does not authorize it or SDD validation.
