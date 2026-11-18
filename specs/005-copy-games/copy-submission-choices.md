# T18 — Catalogue multi-selection and local copy choices

Specification 0.6; approved plan/tasks 0.3. Scoped implementation evidence;
verification complete for T18's scoped completion criteria. No formal SDD validation or native/physical
acceptance is implied.

## Authorization and boundary

The user approved the displayed T18 plan and implementation with “apruebo el
plan y la implementación”. Work starts from clean local `dev` at `f2c4322`, on
`feature/005_T18_copy-submission-choices`, with no commit or push. Scoped T17 is
complete. Earlier mandatory Windows/native prerequisites and physical T10 stay
open; this task adds no new native dependency exception.

The catalogue now has a separate `SelectedGames` collection and explicit prepare/
send commands. Existing details/edit/delete continue to use `SelectedGame`.
The new typed submission view prepares one source reader/device, mounted volume
and PSP for each game. A small UI converter binds single/multiple selection to
the composition boundary; views contain no business rules or new event logic.

An internal enable method is used only by controlled tests/preview in T18.
Production keeps its existing single-copy entry and single-selection mode.
T19 must activate the batch composition after aggregate shutdown is connected;
removing the old IsBusy guard alone is not activation. No persistent queue,
association/database write, native provider, mount/authorization change, new
package or archive/filesystem/resource-engine change is introduced.

## Draft ownership and admission

- Drafts seed choices once from valid existing device preferences and unique
  available candidates, using Core association rules. Selection and preparation
  alone do not start archive I/O. Later draft edits never invoke shared device
  association commands.
- Source and PSP choices retain identity/media evidence, and volume choices retain
  identity/root evidence. Refresh keeps an unavailable placeholder instead of
  silently choosing another reader, volume or PSP. Media replacement requires
  explicit reselection. Device recovery can make a draft ready, but cannot send
  it automatically. Missing PSP choices report a destination incident.
- The device view model exposes its latest immutable UI-projected observation;
  caller-owned attachment/replacement manages the presentation subscription.
  Refresh and send validate against that observation, including provider errors.
  Core capture/admission remains the authority for a newer observation, removal
  reservation, identity/readiness and close gate at actual registration.
- Each send assigns fresh operation IDs. Drafts validate explicit choices with
  Core rules and an empty local preference overlay, so clearing a choice cannot
  reuse a shared preference. Ready submissions go to the coordinator immediately;
  the command does not await transfers or use a lifetime-long async-command gate.
- Prevalidation and coordinator incidents are mapped back to the original batch
  positions. Accepted drafts are removed, unresolved drafts stay visible with
  their individual incident and a batch summary. Correction requires another
  explicit send. Re-preparing the same selected game can create a new operation
  for a second PSP while the first still runs.
- Catalogue refresh snapshots selected IDs before publishing the replacement
  list, because control bindings can change selection during notification. It
  reconciles selected records and pending drafts by ID, removes deleted drafts
  and updates changed draft metadata. Captured operations are never reread or
  retargeted by selection, refresh, edit or deletion.
- ComboBox refresh snapshots old choices before replacing item collections, then
  restores canonical choices/placeholders. Synchronous binding callbacks cannot
  erase a valid selection while options are being published.

## Traceability and controlled tests

| Behavior | Requirements / acceptance | Evidence |
| --- | --- | --- |
| One/many selected games, explicit prepare/send, independent catalogue focus | FR-1, FR-11; AC-1, AC-6, AC-9; NFR-2/NFR-3 | Initial missing-selection test failed before implementation; awaited headless Ctrl+A selection, typed controls, focused edit/delete and minimum-size workspace checks. No I/O from selection/preparation. |
| Local reader/volume/PSP choices without preference writes | FR-2, FR-3, FR-16, FR-20, FR-21; AC-2, AC-15, AC-20, AC-21; NFR-1 | Preference seeding, independent drafts/readers/PSPs, multiple volumes, missing/replaced media, explicit reselection and provider-error checks. Full source Location uses existing Core path validation. |
| Partial admission, indexed incidents and explicit resubmission | FR-23; AC-23 | Mixed ready/unready batch, correction without automatic I/O, a source reserved for controlled removal, original incident indices after filtering, and an independent accepted game without rollback. |
| Concurrent additions and immutable captured requests | FR-11, FR-20, FR-21; AC-6, AC-9, AC-20, AC-21; NFR-2/NFR-4 | Real session/coordinator/engine with controlled byte I/O: second submission while first is pending, same game to another PSP, shared reader/disc or different readers, no catalogue writes or association mutation. Existing source-integrity/native resource tests remain authoritative. |
| Catalogue/CSV/device compatibility | NFR-1/NFR-3/NFR-4; AC-1, AC-2, AC-20 | Catalogue refresh with and without a rendered view, selected-ID reconciliation, one-record edit/delete, live option-label refresh without lost choices, independent row binding, and full solution regression. |

Controlled fixtures model devices and byte I/O; they do not prove actual optical
readers, media correspondence, PSP filesystems or native ejection. The controlled
memory-file map is synchronized for overlapping test access, and the fixture can
publish device observations; production adapter policy is unchanged.

## Development observations

The initial behavioural test failed because `SelectedGames` did not exist.
The first new functional compilation exposed a missing Avalonia extension-method
namespace in the test. A subsequent focused run passed 91 cases and found that
style-based mode configuration left the catalogue in Single mode; an explicit
converter binding fixes it. The next run passed 91 cases and caught a test trying
to focus the nonfocusable ListBox itself; keyboard selection now starts from its
focusable item, as a user would. A mounted-refresh test then failed because the
selected set shrank to one record during list publication; selected-ID capture
before publication fixes that behavior. The following focused run passed all
93 then-present cases. A final indexed-coordinator-incident case is included in
the verification below. Existing copy assertions remain intact.

## Verification and remaining gates

Full rebuild (`dotnet build PSPMan.slnx --no-restore --no-incremental
--disable-build-servers`) and post-coverage builds passed with zero warnings and
errors. Packages are unchanged; restored assets are reused. Tests use xUnit
2/VSTest and .NET SDK 10.0.404, with positional solution/project paths.

Full solution regression passed **2,024 tests**, zero failures and **37 Windows
discovery skips** (Core 795, Infrastructure 820, Desktop 409). It includes all
17 new T18 cases and existing catalogue/CSV/device/copy coverage. The command is
`dotnet test PSPMan.slnx --no-build --no-restore --disable-build-servers`.

Sequential focused coverage passed **94 cases** (17 new plus 77 retained copy
cases), using:

```sh
dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~CopySubmission|FullyQualifiedName~GameCopy|FullyQualifiedName~ConcurrentCopy' --collect:'XPlat Code Coverage' --results-directory <temporary-results> -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include='[PSPMan.Desktop]*'
```

Owning-project Cobertura source projection, merged by line across generated
classes:

| Source | Lines | Branches |
| --- | --- | --- |
| CopySubmissionViewModel | 107/107 (100%) | 78/86 (90.70%) |
| MainViewModel.CopySubmissions | 78/84 (92.86%) | 56/66 (84.85%) |
| CopySelectionModeConverter | 1/2 (50%) | 4/4 (100%) |

Remaining lines are the defensive admission-exception mapping for closure/
disposal races and the intentionally unsupported reverse UI conversion.
Remaining branches include defensive identity/choice/empty-collection guards.
These remain in the denominator; no extra mirror tests were added for an
unsupported reverse binding. SHA-256 checks confirm both Desktop output copies
were restored byte-for-byte after coverage, before recompilation and graphical
execution. The final shorter prepare-button label changes XAML only; measured
view-model/converter sources are unchanged.

The final post-coverage regression, including the shorter prepare-button label,
also passed 2,024 tests with zero failures and 37 Windows discovery skips.

Native Avalonia/X11 inspection used a throwaway preview with three draft games,
two logical mounted optical readers and two PSP choices, backed by the controlled
test session/repository/I/O. Screenshots were inspected at 900 × 450: title and
path wrapping, independent reader/volume/PSP controls, a fixed send action and
scrolling to the final draft remain usable. The prepare-button label was shortened
to fit the catalogue column. A preview-only focus shortcut selected the final PSP
control for viewport inspection; no product shortcut was added. Actual Ctrl+A
selection and focusable command/choice behavior are covered by awaited headless
tests. Closing the preview disposed the controlled session and exited 0 without
using real media. No scratch preview or screenshots are added to the repository.

Production startup, resize to 900 × 450 and normal close also passed with exit 0,
using `dotnet run --project src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build
--no-restore`, a temporary catalogue/config/cache and private D-Bus verified
unable to activate UDisks2. No real device discovery/mount/copy/removal was
performed. Ambient portal/FUSE messages did not represent an application
unhandled exception. Editor diagnostics, whitespace and documentary links pass.
The inherited [native pending gates](./pending-validation.md) and
[physical matrix](./manual-validation.md) remain mandatory. T19 requires its own
scoped plan and explicit authorization. Formal SDD validation remains unauthorized.
