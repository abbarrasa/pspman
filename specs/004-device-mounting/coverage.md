# Device mounting — Build, regression and coverage

Status: **T48 measurement complete; native acceptance remains pending.**

Specification: approved [revision 0.3](./spec.md); [plan](./plan.md) and [tasks](./tasks.md) revision 0.1.
Measured baseline: `3a2727cceecc9a0d983d6102e77deae5e9e4f83c` (local dev, T47 included).
Final Desktop measurement includes the explicitly authorized test-only netstandard-facade allowance described below; production remains identical to this baseline.
Branch: `feature/004_T48_build-regression-coverage`.

## Environment and scope

- Linux x64, .NET SDK 10.0.403, net10.0, Debug; VSTest, Microsoft.NET.Test.Sdk 17.14.1, xUnit 2.9.3, coverlet.collector 6.0.4 and Avalonia.Headless 12.1.4.
- No global.json, Directory.Build.props or Directory.Packages.props overrides were found. No packages, production code, coverage exclusions or thresholds were changed. The only test change is the user-authorized exact netstandard-facade allowance; no test was excluded or added to raise coverage.
- Ordinary regression finished before coverage started. Each complete owning suite was then instrumented separately, sequentially, with no concurrent test/application using its binaries. Coverage runs unset DISPLAY and WAYLAND_DISPLAY and use existing headless/fake-native fixtures, not the ordinary device-mounting application startup.
- This is execution evidence, not acceptance of native Windows/Linux behavior, PSP model coverage or a completed formal feature review.

## Commands and actual results

From the repository root:

```bash
dotnet --version
git rev-parse HEAD
dotnet build PSPMan.slnx --no-restore --nologo
dotnet test PSPMan.slnx --no-build --no-restore --nologo
```

Build passed with **zero warnings and zero errors**. Ordinary regression passed **1434**, failed **0**, skipped **4**, total **1438**: Core 619 passed, Infraestructure 503 passed/4 skipped, Desktop 312 passed.

Coverage commands were executed in this order, waiting for each to exit:

```bash
env -u DISPLAY -u WAYLAND_DISPLAY dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --no-build --no-restore --nologo --collect 'XPlat Code Coverage' --results-directory TestResults/004-T48/Core --logger 'trx;LogFileName=core.trx' -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura,json
env -u DISPLAY -u WAYLAND_DISPLAY dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --no-build --no-restore --nologo --collect 'XPlat Code Coverage' --results-directory TestResults/004-T48/Infraestructure --logger 'trx;LogFileName=infrastructure.trx' -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura,json
env -u DISPLAY -u WAYLAND_DISPLAY dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --no-build --no-restore --nologo --collect 'XPlat Code Coverage' --results-directory TestResults/004-T48/Desktop --logger 'trx;LogFileName=desktop.trx' -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura,json
```

After the authorized test adjustment, the build and ordinary full regression commands above passed again with the same counts and zero warnings/errors. Then the complete Desktop suite was collected again, after ordinary regression exited:

```bash
env -u DISPLAY -u WAYLAND_DISPLAY dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --no-build --no-restore --nologo --collect 'XPlat Code Coverage' --results-directory TestResults/004-T48/Desktop-final --logger 'trx;LogFileName=desktop.trx' -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura,json
```

The passing Core and Infraestructure measurements remain applicable: neither their tests nor any production code changed. Final Desktop totals use only the final passing run, not a merge with the initial failed run.

| Instrumented suite | Passed | Failed | Skipped | Exit | Evidence status |
| --- | ---: | ---: | ---: | ---: | --- |
| Core.Tests | 619 | 0 | 0 | 0 | Successful measurement |
| Infraestructure.Tests | 503 | 0 | 4 | 0 | Successful measurement; Windows-native gaps retained |
| Desktop.Tests, initial | 311 | 1 | 0 | 1 | Historical failed measurement, superseded below |
| Desktop.Tests, final | 312 | 0 | 0 | 0 | Successful measurement including the adjusted architecture assertion |

Raw JSON/Cobertura attachments are retained under the ignored `TestResults/004-T48/` directory:

- Core: `Core/92618c9c-8760-4f68-b18a-05b16448f705/`.
- Infraestructure: `Infraestructure/7fc74862-7b6e-4df9-b401-f8342735276f/`.
- Desktop, historical failure: `Desktop/6efe0827-2e7c-4bf4-bf6c-c965f5403723/`.
- Desktop, final passing run: `Desktop-final/bd1c2af4-cb74-4351-b08e-57902f8398ce/`.

TRX files are at each suite directory root. Attachment copies in TRX subdirectories are not independent measurements.

## Instrumentation failure and authorized resolution

[CoreReferencesNeitherUiPersistenceNorPlatformAssemblies](../../tests/PSPMan.Desktop.Tests/DeviceBoundaryTests.cs) originally permitted only System-named assembly references. In the initial Desktop coverage run, Core additionally referenced `netstandard, Version=2.0.0.0`, which Coverlet's instrumentation introduces. The assertion rejected that standard facade, not an Avalonia, SQLite or platform dependency. All other 311 Desktop cases passed in that run.

After collection exited and the collector restored binaries, the following diagnostic passed 1/1 without rebuilding or modifying the test:

```bash
dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --no-build --no-restore --nologo --filter 'FullyQualifiedName~DeviceBoundaryTests.CoreReferencesNeitherUiPersistenceNorPlatformAssemblies'
```

Ordinary full regression also passed that assertion before instrumentation. This isolates an instrumentation/architecture-test compatibility issue, not a production dependency defect. Work paused as promised before correction. The user then explicitly authorized adjusting this test. The assertion now permits exactly the standard assembly name `netstandard` alongside its existing System-name rules, with a comment explaining Coverlet. Rejection of other dependencies and all other architecture/IL checks are unchanged. No test was excluded or filtered out of collection. Rebuild, full ordinary regression and all 312 instrumented Desktop cases subsequently passed. The final counts equal the earlier diagnostic counts, but now come from a passing suite. T48's measurement gate is complete without certifying pending native acceptance.

## Owning-suite project totals

Only the production module owned by each suite is counted: Core.dll from Core.Tests, Infraestructure.dll from Infraestructure.Tests, Desktop.dll from Desktop.Tests. Referenced production modules measured by another suite are not merged or added. These whole-project totals include existing catalog/CSV code as well as devices.

| Production project | Lines covered/total | Lines | Branches covered/total | Branches | Status |
| --- | ---: | ---: | ---: | ---: | --- |
| Core | 1688/1760 | 95.91% | 793/876 | 90.53% | Passing suite |
| Infraestructure | 2650/3586 | 73.90% | 1378/2209 | 62.38% | Passing suite, 4 native skips |
| Desktop | 1900/2043 | 93.00% | 841/944 | 89.09% | Passing final suite |

Integer counts come from Coverlet JSON: sum method Lines entries and their positive hit counts, and Branches entries and their positive Hits, including generated state-machine entries. This preserves Coverlet's counting units rather than deduplicating source line numbers across methods. Core counts match all four Cobertura root counts; project rates agree with their owning Cobertura package rates up to truncation/rounding. Percentages above are recomputed from integer counts, rounded to two decimals. Do not recover integer branch totals by multiplying truncated package rates or solely summing source-line condition summaries.

Reproducible owning-module aggregation (supply the module name and JSON attachment from the corresponding run):

```bash
jq --arg module 'PSPMan.Core.dll' '
  [ .[$module][][][] ] |
  {lines: ([.[].Lines | length] | add),
   covered: ([.[].Lines[] | select(. > 0)] | length),
   branches: ([.[].Branches | length] | add),
   hit: ([.[].Branches[] | select(.Hits > 0)] | length)}
' TestResults/004-T48/Core/92618c9c-8760-4f68-b18a-05b16448f705/coverage.json
```

## Device-specific subsets

These are subsets of the same owning measurements, not extra executions or additive totals.

| Subset | Lines covered/total | Lines | Branches covered/total | Branches |
| --- | ---: | ---: | ---: | ---: |
| Core Devices directory | 812/867 | 93.66% | 493/564 | 87.41% |
| Infraestructure Devices directory | 2318/3244 | 71.45% | 1332/2155 | 61.81% |
| Infraestructure Devices/Linux | 741/922 | 80.37% | 214/308 | 69.48% |
| Infraestructure Devices/Windows | 1476/2216 | 66.61% | 1058/1777 | 59.54% |
| Desktop device-specific files | 735/743 | 98.92% | 369/400 | 92.25% |

Desktop subset includes ApplicationDeviceLifetime, DeviceStartup, DeviceWorkspaceWidthConverter, DeviceItemViewModel, all four DevicesViewModel source files, MainViewModel.Devices, PspDestinationOptionViewModel, SourceChoiceViewModels, DevicesView.axaml and its code-behind. Shared App/Program/MainWindow/catalog files remain in whole-project totals but are not wholly attributed to device-only work. Infrastructure directory totals also include shared factory/bridge code, so Linux plus Windows is not the entire device-directory total.

## Significant gaps and acceptance limits

- Core has 72 uncovered lines and 83 uncovered branches overall; devices account for 55 lines and 71 branches. DeviceSessionService accounts for 44 uncovered lines/46 branches, DeviceAssociationRules 8/21, MountedGamePathRules 2/4 and snapshot records 1 line. Observer/operation exceptional and cancellation paths and defensive reconciliation conditions are not exhaustive. Recognition has 37/37 lines and 20/20 branches executed, but fixture execution is not all-model hardware certification. Existing CSV defensive paths contribute to the remaining project gaps.
- Infraestructure has 936 uncovered lines and 831 uncovered branches; 926 lines/823 branches are in devices. Important native gaps include WindowsUsbAncestry 0/65 lines, WindowsOperationNativeProcess 0/44, WindowsOpticalNativeApi 0/58 and WindowsRemovalNativeApi 0/42. WindowsDeviceApi executes 38/196 lines, WindowsVolumeMountApi 16/85 and WindowsOperationNativePipe 11/90. Passing fake API/protocol tests do not execute those kernel/token/ACL/USB/UAC paths. Native guards exercised on Linux must not be described as successful native Windows operations.
- Linux coverage includes the real D-Bus transport against an isolated fake UDisks service, not real-device mounting or Polkit authorization. TmdsUDisksTransport executes 187/244 lines, UDisksClient 66/113, LinuxUsbMetadataReader 79/103 and LinuxSessionDeviceProvider 103/125. Exception/service, metadata ancestry and lifecycle branches remain incomplete. Native service availability, real media, hardware identities and authorization agents require separate observations.
- Final passing Desktop totals leave 143 lines/103 branches uncovered. App initialization/XAML and ViewLocator are unexecuted; Program executes 1/10 lines, and native file/folder pickers are partial. The device-specific subset leaves 8 lines/31 branches, although ApplicationDeviceLifetime executes all 86 lines and still leaves 3 of 26 branches uncovered. Headless controls and test-app lifetime evidence do not certify ordinary native startup/dialogs.
- Four WindowsNativeReadOnlyIntegrationTests are skipped: real storage enumeration, native interface registration/disposal, observer registration before inventory, and cleanup after inventory failure. Windows VM/CI/native execution, UAC/apphost/security harnesses, PSP family/storage presentation coverage, real busy/eject/disconnect/reconnect and Linux Polkit remain pending in [the native runbook](./pending-validation.md) and [manual/model matrix](./manual-validation.md). T49–T53 were not started. No native application smoke test was repeated: unchanged production startup can mount arbitrary attached media.
- No whole-feature acceptance, numeric performance guarantee, process-wide filesystem interception or exhaustive security certification is inferred from coverage. [Task evidence](./tasks.md) retains requirement mappings and the separate T45–T47 functional/boundary results. T48 is complete after the authorized test-only compatibility correction; no production repair was needed. Editor diagnostics and git whitespace checks passed. No commits or pushes were made; T49 is not started.
