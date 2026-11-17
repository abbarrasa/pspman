# T09 — Confirmed shutdown and lifetime races

Specification 0.5, plan/tasks 0.2. This report covers the authorized T09
implementation. It is not whole-feature validation, Windows native certification
or physical PSP acceptance.

## Authorization and scope

The user requested the next task, reviewed its scoped plan and explicitly
approved it with “Apruebo el plan”. Work started from clean local dev at c2cc385
on `feature/005_T09_confirmed-copy-shutdown`, depending on completed T08.
Earlier Windows and physical gates remain mandatory/open. No new package,
provider policy, catalogue mutation, commit or push was introduced. T10 and SDD
validation are not started or authorized by this task.

## Implementation and decisions

- [GameCopyViewModel.Shutdown](../../src/PSPMan.Desktop/ViewModels/GameCopyViewModel.Shutdown.cs)
  exposes one pending close decision. It does not cancel the copy while the user
  is deciding. Decline preserves the active copy, its password/overwrite input,
  device state and application. Accept closes admission to new starts and input
  responses, then the owner cancels/drains the operation before device detachment.
- [CopyCloseConfirmationViewModel](../../src/PSPMan.Desktop/ViewModels/CopyCloseConfirmationViewModel.cs)
  captures one confirmation lease. Its English message explains that written
  files remain, may be incomplete/incorrect and overwritten files are not
  restored. Retired commands cannot answer a new close request. The destination
  is displayed when already available from preparation/progress.
- [GameCopyView](../../src/PSPMan.Desktop/Views/GameCopyView.axaml) presents that
  typed confirmation above the copy listing. Warning/destination text scrolls
  within a bounded area; both decision buttons remain visible at 900 × 450,
  including an unusually long destination. Keyboard activation and automation
  naming use existing controls; view code-behind still initializes only.
- [MainViewModel.Copy](../../src/PSPMan.Desktop/ViewModels/MainViewModel.Copy.cs)
  observes CanStart, withholding a new copy while a close decision is pending or
  accepted. If the current copy finishes while confirmation is open, the user
  still chooses whether to close. Decline restores admission; acceptance never
  cancels a subsequent operation because none can start in that interval.
- [ApplicationDeviceLifetime](../../src/PSPMan.Desktop/ApplicationDeviceLifetime.cs)
  remains the single owner of window/desktop shutdown. Repeated close requests
  share one decision/completion. Decline resets the pending shutdown request.
  Acceptance awaits copy cancellation and file closure while presentation/devices
  remain attached, then uses the existing controlled disposal/exit path. A
  provider-cleanup failure still exits once with code 1. Neither current output
  nor previously completed output is deleted/restored.
- [App](../../src/PSPMan.Desktop/App.axaml.cs) already routes ShutdownRequested
  to that same owner and removes its handler before final desktop.Shutdown.
  This wiring required no second handler or source change. Tests exercise the
  shared RequestShutdown entry point and actual Window.Close events; they are
  not evidence of a real operating-system session shutdown.

Initialization and disposal reserve their completion tasks before invoking
factories, UI observers or cancellation callbacks. Reentrant callers therefore
share the same task, rather than starting competing cleanup. Construction and
each UI attachment recheck stopping; late state cannot attach or start discovery.
Cleanup awaits initialization and drains the copy owner before disposing its
caller-owned device session, including when initialization/cleanup fails.
Explicit programmatic disposal remains unconditional: it retires pending input
and close decisions and releases owned operations without requiring a UI answer.

An optional copy-composition factory on the existing lifetime owner permits
controlled archive/filesystem contracts in functional tests. Production still
uses CopyStartup and the existing adapters. No DI framework, native helper,
archive engine, Core policy, schema or persistence change is included.

## Test-first evidence

The initial focused build failed for the missing confirmation properties and
copy-factory argument. The first ten close cases then passed. Additional tests
found a long destination pushing the decision buttons outside the minimum
window; bounded text scrolling and a separate button row corrected it.

A later regression reproduced two different cleanup tasks when a UI detachment
observer reentered DisposeAsync. Reserving the shared task before notifications
corrected that failure. Startup also reserves its barrier before executing
construction, so reentrant disposal cannot miss a late copy owner. Existing
initialization/cleanup behavior is retained behind those barriers.

One intermediate test compile needed Avalonia's geometry extension namespace.
Input-retirement assertions were corrected to await visible UI projection,
consistent with the existing asynchronous presentation contract; engine/session
completion alone does not mean a queued UI clear has already run. These
intermediate failures are historical, not passing evidence.

The **28 new awaited shutdown cases** cover:

- decline/accept during I/O, password, overwrite and ambiguous encrypted input;
- duplicate requests, renewed confirmation after decline and retired commands;
- file release before device disposal/exit, retained partial/overwritten bytes
  and preservation of actual earlier completed-copy output;
- a copy completing while confirmation is open and admission remaining closed;
- programmatic disposal during confirmation/input, native-style decoding or
  removal callbacks that ignore cancellation until explicitly released;
- safe removal during a close decision, stop-before-removal ordering and visible
  native-style veto errors;
- selection changes during password/ambiguous input and no restart from old input;
- cleanup failure with one error exit, no-copy closure without confirmation,
  bounded minimum-size controls and keyboard decline;
- disposal during copy construction/attachment/device attachment, no late
  discovery, and reentrant disposal sharing its reserved completion task.

The focused run also includes existing lifetime, composition and input tests:
**46 passed**, with no skips. Existing late-provider tests explicitly assert
that copy state remains detached after shutdown.

```sh
dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --filter 'FullyQualifiedName~GameCopyShutdownTests|FullyQualifiedName~ApplicationDeviceLifetimeTests|FullyQualifiedName~GameCopyCompositionTests|FullyQualifiedName~GameCopyPresentationTests' --no-restore --collect:"XPlat Code Coverage" --results-directory <isolated-results>
dotnet build PSPMan.slnx
dotnet test PSPMan.slnx
```

Build passed with **zero warnings/errors**. Full regression passed **1,850**:
Core 734, Infraestructure 739, Desktop 377; **26 existing Windows-only skipped
discovery entries** remain. Existing copy, catalogue, device, architecture and
native adapter tests passed. No new tests are silently platform-skipped.

## Scoped coverage

Coverage was collected sequentially before rebuilding and running noninstrumented
regression/graphical checks. No other test/application used instrumented binaries.
The table merges duplicate source lines across nested/generated state machines;
no production coverage exclusion was added.

| Owning file | Lines | Branches |
| --- | --- | --- |
| GameCopyViewModel.Shutdown | 34/34 (100%) | 23/26 (88.46%) |
| CopyCloseConfirmationViewModel | 11/11 (100%) | None reported |
| ApplicationDeviceLifetime | 128/130 (98.46%) | 47/52 (90.38%) |
| GameCopyViewModel, focused filter | 161/171 (94.15%) | 83/100 (83%) |
| MainViewModel.Copy | 17/17 (100%) | 10/12 (83.33%) |

The two uncovered lifetime lines are defensive initialization-barrier and
confirmation/stop exception catches. Native-style failures are represented by
typed results, cooperative stop and controlled noncooperative callbacks, rather
than injecting arbitrary exceptions from UI observers. Uncovered branches include
defensive repeated/obsolete calls; the main copy-model percentage is for this
focused filter, not all T08 paths. These figures do not measure native hardware,
forced process termination or operating-system shutdown coverage.

## Graphical verification and limits

A temporary native Avalonia/X11 preview reused the exact controlled-I/O shutdown
fixture, with the production MainWindow, GameCopyView and lifetime owner. It used
the existing test assembly, synthetic ready providers and memory output, without
native device discovery, a real archive or a PSP. No debug behavior was added to
the product. A private D-Bus and separate XDG paths isolated desktop startup.

At **900 × 450**, actual Alt+F4 displayed the retention warning, destination and
both decision buttons. Clicking Keep application open removed confirmation and
left the same row Transferring at the same partial byte count. A second Alt+F4
displayed a fresh confirmation; Stop copy and close drained the controlled copy
and exited **0**. The images were visually inspected; warning and button text
were visible in both light/dark system-theme projections. This proves native UI
interaction with controlled I/O, not native archive/PSP transfer or real OS logout.

The ordinary production startup/close check uses:

```sh
dotnet run --project src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build --no-restore
```

Run only under isolated temporary XDG paths and a private system-bus override
whose activatable names exclude UDisks2. This prevents ordinary automatic mounting
against arbitrary hardware. The owned production window was observed at
1200 × 650, resized/captured at 900 × 450 and closed normally with Alt+F4,
without confirmation when no copy was active. Exit was **0**, with no unhandled
application exception and no physical media access. Private portal/FUSE/keyring
diagnostics do not constitute application or PSP failures.

## Mandatory follow-up

T01/T03/T05/T06 Windows and T10 physical gates remain open. Follow
[pending validation](./pending-validation.md) for designated Linux/Windows source
media and real PSPs: close decline/accept during actual reads/writes, password,
overwrite and FR-19 input; retained hashes; previous-copy preservation; release
before actual device removal; native veto/privilege callbacks and packaged engine
lifetime. Windows graphical behavior, OS shutdown requests and forced termination
are not inferred from Linux/headless results. T09 completion is scoped to its
automated/lifetime/UI criteria; whole-feature acceptance is still unverified.
Next: report T09 and stop. Present T10's own plan and obtain explicit approval
before consolidation or SDD validation.
