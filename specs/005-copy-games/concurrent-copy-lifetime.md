# T19 — Production concurrent submission and aggregate confirmed closure

Specification 0.6; approved plan/tasks 0.3. Scoped implementation and available
verification are complete. This report does not constitute formal SDD review,
Windows acceptance or physical PSP/media validation.

## Authorization and scope

The user requested the next task and approved its displayed scoped plan with
“Apruebo el plan”. Work starts from clean local `dev` at `9f116e4` on
`feature/005_T19_concurrent-copy-lifetime`. No commit or push is authorized or
performed. Dependencies T18 and scoped T09 are complete; T16 supplies operation
ownership and device-removal boundaries. Existing native Windows and physical
validation gates remain mandatory. No specification or dependency exception is
introduced.

Production now enables preparation when copy presentation attaches. The catalogue
uses separate multi-selection and explicit preparation/send commands; each send
adds captured requests immediately, using the existing coordinator, engine and
one shared `CopyDestinationResources` owner. Draft correction never auto-sends.
The old catalogue copy command/button and selection-to-copy mutation connection
are removed. The internal Desktop single-operation adapter and existing Core
single-call APIs remain for historical regression harnesses; production does not
invoke them. No archive, resource-accounting, device policy, persistence or
package changes are required.

## Closing and ownership

- Opening a decision calls `BeginClose` before consulting active ownership. The
  Core registry is authoritative even if UI rows/busy state have not yet been
  published. With no active owner, admission still closes before UI observers
  can submit reentrantly; pending removals are drained during shutdown.
- One identity-bound aggregate confirmation describes all active copies and
  their PSP roots. Repeated requests share it. Decline preserves row input,
  existing work and devices, then reopens Core admission before notifying the
  commands. Old confirmation commands cannot answer a later decision.
- Pending drafts are temporarily hidden during the decision/stopping, retaining
  their choices and reappearing on decline. This reserves enough vertical space
  for both close choices at the minimum 900 × 450 window size.
- Acceptance marks rows Stopping, retires their input and resolves the Core
  decision. Every nonterminal owner and already-reserved removal handoff must
  drain. Completion during the decision cannot reopen admission. Late row
  creation while closing also enters Stopping and cannot obtain new input.
- Stopping is not terminal: a native callback may still own output. Its actual
  return, archive session disposal and filesystem/resource release precede copy
  detachment and device/provider disposal. UI snapshots and progress are never
  used as a release barrier.
- Programmatic disposal remains unconditional, coalesced and reserved before
  callbacks. Initialization/late startup guards remain in place. Presentation
  stays attached until copy release even on this path; devices/providers are
  disposed afterward. Cleanup errors retain the controlled nonzero exit path.
  Before any copy owner exists, initializing presentation is detached immediately
  while cancellation and late provider construction/disposal are still awaited.
- No cancellation/failure/close removes or restores destination output. Earlier
  completed files, partial writes and overwritten contents remain.

## Traceability and evidence

| Behavior | Requirements / acceptance | Verification |
| --- | --- | --- |
| Production explicit batch addition, one resource owner, immutable per-copy rows | FR-11; AC-9; NFR-1–NFR-4 | Production attachment/preparation test; retained T18 local choices/partial sends and T17 row tests; default native resource composition exercised by native close tests. |
| Aggregate identity-bound confirmation, decline/accept and no late admission | FR-17, FR-24; AC-16, AC-24 | Four two-owner input/transfer stages, repeated/stale decisions, completed owners while deciding, empty active set and queued UI publication tests. |
| Independent input, cancellation/failure and retained output | FR-9, FR-10, FR-19, FR-24; AC-7, AC-8, AC-19, AC-24 | Two simultaneous password/overwrite/FR-19 inputs; retained per-row cancellation/error coverage; explicit stopping/release tests. |
| Shared device removal, startup/disposal and native callback ownership | FR-16, FR-17; AC-15, AC-16; NFR-1/NFR-4 | Retained removal/startup/reentrant/cleanup-error tests plus actual packaged Linux 7-Zip and native filesystem callbacks under aggregate and programmatic close. |

Five initial new cases failed against the old production implementation: the
preparation flag was disabled and the Core admission gate remained open during
confirmation. Subsequent focused checks exposed stale UI-input assertions and
an obsolete single-slot Stopping assertion; the tests now await visible input
retirement and inspect the owning row. Aggregate shutdown explicitly marks rows
Stopping. Wider diagnostic runs identified a historical queued-progress harness
that still depended on the removed selection bridge; it now explicitly sets the
retained adapter's test selection. The missing-PSP composition assertion now
checks the typed incident and latest game identity. An early-startup close test
exposed delayed detachment before a noncooperative provider returned; immediate
detachment is preserved when no copy service exists. These aborted/failed runs
are not passing evidence. Both minimum-window cases also failed with pending
drafts obscuring the close buttons, before temporary draft hiding was added.
Their corrected viewport, keyboard-decline and draft-retention checks pass.

The Linux native matrix uses two real native archive sessions and filesystem
roots in a temporary fixture, with controlled device inventory. It pauses a real
native output callback after one accounted byte. Cases cover two independent
writes and a shared-file waiter, each under confirmed decline/accept and
unconditional programmatic disposal. Before releasing the callback, shutdown
must remain pending with presentation/devices attached and provider undisposed.
Provider disposal asserts both native extractions returned, both sessions were
disposed, presentation detached and no descriptor below the fixture remains.
Partial output, prior files and source SHA-256 hashes are checked. This is native
software evidence, not actual PSP recognition, mounting or ejection evidence.

## Verification and remaining gates

The final lifetime-focused run passes **55 cases**. Sequential Desktop coverage
passes **422 tests**, including all **13 new T19 cases**, with zero failures.
SHA-256 checks confirm the Desktop application/test DLLs are restored byte for
byte after instrumentation, before the post-coverage root build.

| Owning source | Covered lines | Covered branches |
| --- | --- | --- |
| ApplicationDeviceLifetime | 142/144 (98.61%) | 51/56 (91.07%) |
| Aggregate shutdown presentation | 52/53 (98.11%) | 41/50 (82.00%) |
| Copy close confirmation | 11/11 (100%) | No branches |
| Operation row | 126/126 (100%) | 126/130 (96.92%) |
| Row collection/routing | 71/72 (98.61%) | 58/74 (78.38%) |
| Main copy composition | 19/19 (100%) | 12/12 (100%) |
| Draft preparation/submission | 81/87 (93.10%) | 62/70 (88.57%) |

Uncovered lines are defensive initialization/stop/decision exception handling,
unknown-operation routing, and the inherited admission-exception mapping. The
retained internal single-operation adapter measures 123/130 lines (94.62%) and
65/74 branches (87.84%); its generic startup/cancellation/teardown exception
paths remain defensive gaps. Conditional branch gaps include already disposed,
missing coordinator and stale/race guards. No artificial coverage threshold or
claim of physical acceptance is introduced; actual provider-cleanup failure,
pending removal, callback drain, stale decision and startup races are exercised.

A controlled native X11 application launch at 900 × 450 exercises two active
copies plus a pending draft, using real Avalonia views and bound close buttons.
Rendered window captures were visually inspected. Both choices fit after
temporarily hiding drafts; decline preserves both copies/choices, an old command
cannot accept the next decision, and acceptance releases both operations and the
provider exactly once, retaining both one-byte files and exiting 0. Keyboard
activation is covered by the minimum-window headless test. Native graphics tests
use controlled inventory/I/O and cannot establish PSP or OS logout acceptance.

Final `dotnet build PSPMan.slnx` passes with zero warnings and errors, including
the post-coverage build. Final `dotnet test PSPMan.slnx
--blame-hang-timeout 60s --blame-hang-dump-type mini --logger trx
--results-directory <temporary-results>` passes **2,037 tests**, zero failures
and **37 Windows discovery skips**: Core 795, Infrastructure 820 and Desktop
422. Tests use xUnit 2/VSTest and .NET SDK 10.0.404. The timeout collector did not
abort the successful run; per-project TRX results retain their separate counts.

Production `dotnet run --project
src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build --no-restore` starts with an
isolated temporary catalogue/config/cache and a private D-Bus verified unable
to activate UDisks2. The owned window receives its normal close request and the
application exits 0, with no unhandled exception. No arbitrary media is mounted.
Final editor diagnostics and `git diff --check` report no errors. No commit or
push is performed; task/evidence/memory updates are included in the working tree.

Native Windows, actual PSP/source devices, Polkit/UAC and OS logout/forced
termination remain unexecuted; see [pending validation](./pending-validation.md)
and final T10. Formal SDD validation/code review requires separate approval.
