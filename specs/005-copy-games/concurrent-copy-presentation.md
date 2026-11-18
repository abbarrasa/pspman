# T17 — Typed concurrent copy rows and independent input

Specification 0.6; approved plan/tasks 0.3. Scoped implementation evidence;
verification complete for T17's scoped completion criteria. This is not formal SDD validation or physical
PSP/native Windows acceptance.

## Authorization and scope

“Apruebo el plan y su implementación” approved the presented T17 plan. Work starts
from clean local `dev` at `3cb62ea` on
`feature/005_T17_concurrent-copy-rows`, with no commit or push. T17 depends on
completed scoped T16/T08; inherited unavailable Windows and physical checks
remain mandatory. No additional native dependency exception is introduced.

The collection owns typed `CopyOperationViewModel` rows and routes each
`IGameCopyInteraction` request by operation ID. Each row retains its captured
request, separate transient input/completion source and cancel command. Source
presentation uses the existing platform-specific mounted-path rules. The row
renders captured game/source/PSP, destination, combined actual-byte progress,
resource-wait reason, outcome and retained-output warning.

The production catalogue still invokes the single-copy compatibility start.
An internal, controlled submission entry exercises T16's real concurrent
coordinator in tests; catalogue multi-selection/choices belong to T18 and
production batch activation/aggregate shutdown belong to T19. Existing legacy
properties remain compatibility projections until that integration. Failed
admission/start notifications are separate from accepted rows' terminal messages.
The archive engine, resource accounting, filesystem/native providers, device
policy, persistence and packages remain unchanged. No output cleanup is added.

## Ownership and sequencing

- Every pending request owns its own completion source and command objects;
  password/overwrite/FR-19 requests can coexist across operations. A row refuses
  an operation mismatch, reused prompt, overlapping input for that operation,
  retired operation or accepted closing state. Buttons also check the input kind.
- Responding retires that input; cancellation/failure/disposal clears only its
  owning input. Password text is masked and cleared on answer, token/operation
  cancellation and disposal. It never enters captured requests or snapshots.
- Row progress is posted to the UI and reduced by operation ID and monotonically
  increasing revision. Queued older snapshots cannot replace newer state; terminal
  rows cannot revive. Cancellation retires input immediately while displaying
  Stopping until the coordinator releases the operation and publishes its result.
- Collection mutations, input publication and row projection use the UI posting
  boundary. Disposed rows reject queued publication. Disposal reserves its shared
  completion before callbacks, retires rows outside the collection gate and then
  awaits coordinator disposal before the caller disposes its device session.
- The typed item/input templates keep each command inside its own row. Visible
  rows identify their operation through captured metadata. Progress/password/
  cancellation controls have accessible names; keyboard activation and scrolling
  at 900 × 450 are exercised. No new view code-behind logic or ViewLocator route
  is required: rows use an explicit typed DataTemplate.

## Traceability and behavioral evidence

| Scope | Requirements / acceptance | Verification |
| --- | --- | --- |
| Captured per-operation rows, actual progress and waiting reason | FR-7, FR-11, FR-20, NFR-2/NFR-3; AC-6, AC-9, AC-20 | Real session/engine/coordinator with controlled I/O: add a second copy, explicitly confirm existing-file overwrite, overlap both rows, cancel one and complete the other. Unit projection checks decoded/written counters, destination-file waits and immutable metadata. |
| Independent password, overwrite and ambiguous encrypted input | FR-6, FR-13, FR-19, FR-24; AC-5, AC-12, AC-19, AC-24 | Awaited headless two-row mixed input, masked text, accessible name and Enter activation, owning commands, invalid-kind/retired button rejection, one input cancellation while the other remains pending, and replacement prompt identity checks. |
| Independent cancellation, failure and outcomes | FR-8–FR-10, FR-24, NFR-4; AC-7, AC-8, AC-18, AC-24 | Separate completion IDs, failure cancels only its input, all four terminal outcomes including completed-with-skipped-files, retained-output warnings, stopping until release, no catalogue writes and retained earlier flow coverage. |
| Stale work, teardown and compatibility | FR-9–FR-11, FR-24; AC-7–AC-9, AC-24 | Reversed snapshot callbacks, post-terminal progress, queued input after disposal, accepted-closing response rejection, reentrant shared disposal and existing flow/composition/shutdown cases. |

The initial simultaneous-input test failed against the original single-input
implementation (the second request had already faulted). An early focused run
passed 63 cases and found a stale legacy input alias after cancellation; final
legacy projection now clears that alias. The next run passed 73 cases and exposed
an incomplete test setup: the second overlapping copy correctly requested
overwrite of the existing controlled output. The test now explicitly answers
that request before asserting simultaneous transfer; the following run passed
all 74 then-present cases. Three additional lifecycle cases are included in the
final build and verification below.

Native visual inspection at 900 × 450 exposed unbounded horizontal measurement:
star columns expanded instead of wrapping. Horizontal scrolling is disabled and
the viewport clips scrolling content. Empty row notices are hidden. The Fluent
progress bar's minimum width is explicitly overridden to fit its 160-pixel
column. The real-coordinator headless scenario now renders a 400-character title
and asserts bounded title/bar widths. One intermediate coverage run passed 76
cases but failed a test that demanded scrolling after both prompts were resolved
and hidden; the overflow assertion now checks the simultaneous-pending state.
This preserves the intended shrinking layout instead of requiring empty space.

Existing copy tests keep their assertions. Four control lookups now traverse
rendered template descendants rather than assuming controls are in the root
namescope. The original single-row lookup helper deliberately requires exactly
one matching rendered control; new multi-row tests scope buttons by their input
DataContext, so accidental cross-row lookup cannot silently pass.

## Verification and limits

The complete rebuild and subsequent post-coverage builds passed with zero
warnings/errors. Commands use .NET SDK 10.0.404 and xUnit 2/VSTest positional
project/solution syntax; dependencies are unchanged and restored assets are
reused with `--no-restore`.

Sequential focused coverage passed **77 tests** (14 new concurrency/lifecycle
cases and 63 retained copy cases):

```sh
dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ConcurrentCopy|FullyQualifiedName~GameCopy' --collect:'XPlat Code Coverage' --results-directory <temporary-results> -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include='[PSPMan.Desktop]*'
```

Owning-project Cobertura source projection, merging generated classes by line:

| Source | Lines | Branches |
| --- | --- | --- |
| CopyOperationViewModel | 122/122 (100%) | 118/124 (95.16%) |
| GameCopyViewModel.Operations | 66/67 (98.51%) | 53/68 (77.94%) |
| GameCopyViewModel compatibility/routing | 123/130 (94.62%) | 66/74 (89.19%) |

Remaining gaps include the observer/disposal race catch, defensive admission/
identity guards, legacy start cancellation/unexpected-exception handlers and
the disposal exception handler. They stay in the denominator. SHA-256 checks
confirm both Desktop output copies were restored byte-for-byte after coverage,
before rebuilding. The final bar-width adjustment changes XAML and a layout
assertion only; the measured view-model sources are unchanged.

Final regression after the last layout correction passed **2,007 tests**, zero
failures and **37 Windows discovery skips** (Core 795, Infrastructure 820,
Desktop 392), using `dotnet test PSPMan.slnx --no-build --no-restore
--disable-build-servers`. The final root build passed with zero warnings/errors.

Native Avalonia/X11 inspection used a throwaway preview with three captured
synthetic rows (password decision, overwrite and destination-file wait), no
device provider and no file I/O. Screenshots were inspected before/after the
width correction: titles/state wrap, progress fits its column, scrolling clips
below the header and keyboard Tab navigation reaches lower rows at 900 × 450.
Normal preview close disposes presentation and exits 0. Production startup,
resize and normal close were also exercised through `dotnet run --project
src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build --no-restore`, using a temporary
catalogue/config/cache and private D-Bus verified unable to activate UDisks2.
No physical device discovery/mount/copy/removal was performed. Ambient portal/
FUSE warnings are separate from application exceptions. The native preview
asserts presentation, not archive/device integration. No scratch harness or
screenshots are added to the repository.

Controlled I/O/headless tests prove presentation
and ownership, not actual PSP/FAT media, native archive parallelism, optical
reader behavior, removal/UAC or Windows filesystem acceptance. See the unchanged
[native pending gates](./pending-validation.md) and [physical matrix](./manual-validation.md).
T18 requires its own scoped plan and explicit authorization. Formal SDD validation
remains unauthorized.
