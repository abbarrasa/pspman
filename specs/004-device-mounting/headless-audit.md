# Asynchronous headless dispatch audit

Date: 2026-11-12
Scope: the separately authorized verification follow-up identified during T43, not final feature validation.
Branch: `feature/004_T44_application-device-lifetime`; no commits or pushes.

## Findings and corrections

- Reviewed every `HeadlessUnitTestSession.Dispatch` call in the repository's test sources. Synchronous callbacks are not affected. T43's explicit `Dispatch<bool>` helper already awaits the asynchronous callback correctly.
- Avalonia.Headless 12.1.4 has synchronous `Dispatch<TResult>(Func<TResult>)` and asynchronous `Dispatch<TResult>(Func<Task<TResult>>)` overloads, but no non-generic `Func<Task>` overload. For result-less asynchronous callbacks, inference can select `TResult = Task`, leaving `await` waiting only for the outer `Task<Task>`.
- Five sites (six parameterized test cases) in [DevicesViewTests](../../tests/PSPMan.Desktop.Tests/DevicesViewTests.cs) now call the shared [DispatchAsync helper](../../tests/PSPMan.Desktop.Tests/HeadlessTestDispatcher.cs), which explicitly selects the asynchronous `Dispatch<bool>` overload. Session disposal follows the complete callback, including its assertions and fixture cleanup.
- Seven callbacks in [GameCatalogImportFlowTests](../../tests/PSPMan.Desktop.Tests/GameCatalogImportFlowTests.cs) already return an integer. They now specify `Dispatch<int>` explicitly. The result-less callback defect does not establish that these result-returning callbacks previously skipped their assertions; the current rerun supplies unambiguous evidence.
- Fully awaited execution exposed one incorrect assertion about a retired source button. Avalonia can recycle the presenter for a fresh, enabled choice. The corrected test captures the retired command, verifies it is disabled and cannot apply an association, then verifies the current button remains usable and becomes disabled on disposal. No production change was needed.
- Two [regression checks](../../tests/PSPMan.Desktop.Tests/HeadlessTestDispatcherTests.cs) verify completion after asynchronous suspension on the UI context and propagation of an exception thrown after suspension. A controlled run using the old result-less dispatch pattern failed both checks (premature completion and missing exception); the corrected helper passes both.

## Verification

- SDK 10.0.403, xUnit 2.9.3 and VSTest; no test framework or dependency was added.
- Initial fully awaited audit run: 32 passed, one failed on the recycled-button assertion. All 15 import cases passed. The assertion was corrected as described above.
- An initial externally gated dispatcher regression fixture blocked its own dispatch call; that run was interrupted, and a subsequent isolated diagnostic run was aborted by the 30-second hang collector. The final regression uses a bounded asynchronous delay rather than relying on work scheduled outside an in-progress dispatch. These aborted runs are not passing evidence or product failures.
- `dotnet build PSPMan.slnx --no-restore --nologo`: passed, zero warnings/errors.
- `dotnet test PSPMan.slnx --no-build --no-restore --nologo`: passed 1393 (Core 619, Infraestructure 495, Desktop 279), four Windows-native skips; 1397 total.
- Sequential focused coverage: `dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --no-build --no-restore --nologo --filter 'FullyQualifiedName~DevicesViewTests|FullyQualifiedName~GameCatalogImportFlowTests|FullyQualifiedName~HeadlessTestDispatcherTests|FullyQualifiedName~MainWindowDeviceIntegrationTests' --collect 'XPlat Code Coverage'`: 41/41 passed. SourceChoice, SourceVolumeChoice, SourceSelectionViewModel, PSP destination options/presentation, MainViewModel device integration and DevicesView initialization measured 100% lines/branches. Source synchronous projection measured 99.18% lines / 87.80% branches; its async selection body 95.45% / 80%. Destination synchronous projection measured 100% / 85.93%; its async selection body 90% / 75%. Defensive exception/cancellation branches remain incomplete. This narrower filter is not a replacement for owning-suite or whole-feature coverage, and test-assembly helper code is excluded by the collector; the helper is verified behaviorally by its two regression checks.
- Editor diagnostics and `git diff --check`: no errors.

## Evidence boundaries

The current complete regression supersedes the historical incomplete callback evidence for the audited tests. Historical coverage percentages are not automatically re-certified by a passing test count. No production source, UI markup, persistence, discovery activation or real device operation changed. Native Windows, hardware, Polkit/UAC and all-model acceptance remain pending in the [native validation runbook](./pending-validation.md).

At the AUD-1 handoff, T44 was not implemented and required individual plan approval under the [task execution rules](./tasks.md#scope-and-execution-rules). The user subsequently approved implementation; its separate evidence is recorded under T44. Audit corrections remain in the same task branch as requested.

## Follow-up during T44 regression

The first full T44 regression exposed a separate scheduling race in MainWindowDeviceIntegrationTests: Fixture.Publish waited for Core's published provider revision, then asserted destination state before the UI subscriber had projected the disconnect. The fixture now also awaits the expected visible device keys. All original destination/association assertions remain. The focused 18-case lifetime/window rerun and subsequent complete regression passed; no production rule was changed for this test synchronization correction.
