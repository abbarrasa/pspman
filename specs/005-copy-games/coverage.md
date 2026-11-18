# T10 — Concurrent-copy regression, coverage and runtime evidence

Evidence date: 2026-11-18. Specification 0.6; plan/tasks 0.3.
The user approved the displayed plan and partial continuation with
“Apruebo el plan y la continuación parcial con las pruebas pendientes”.
Baseline: clean local `dev` at `77d5f11`; branch
`feature/005_T10_concurrent-copy-verification`. T10 remains **incomplete** pending
mandatory native Windows and physical evidence. No production/test code,
package, device policy or approved requirement changes are part of this work.
Existing behavioral suites demonstrate the requested verification scope; no
artificial failing test is introduced for documentary consolidation.

The previous 2026-11-17 consolidation concerned specification 0.5/plan 0.2:
1850 passing tests, 26 Windows discovery skips and owning line/branch coverage
96.45%/89.31% Core, 70.09%/59.28% Infrastructure and 93.98%/89.50% Desktop.
Those values are historical and cannot certify the concurrent production flow.
The [current matrix](./manual-validation.md) supersedes obsolete single-slot and
selection-change expectations without changing approved requirements.

## Current automated verification

Debian 13, Linux x86_64/glibc 2.41; .NET SDK 10.0.404, net10.0,
xUnit 2.9.3/VSTest, Microsoft.NET.Test.Sdk 17.14.1 and
coverlet.collector 6.0.4. No global.json, Directory.Build.props or
Directory.Packages.props override or Desktop publish profile/RuntimeIdentifiers
was found. Only the staged `linux-x64` native runtime is available here.

| Check | Actual result |
| --- | --- |
| `dotnet build PSPMan.slnx` before coverage | PASS, zero warnings/errors |
| `dotnet build PSPMan.slnx` after coverage | PASS, zero warnings/errors |
| `PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx --no-build --no-restore --blame-hang-timeout 60s --blame-hang-dump-type mini --logger trx --results-directory <scratch>/regression` | PASS, 2037 passed, zero failed, 37 Windows discovery skips; no hang abort |
| Core regression | 795 passed, zero skips |
| Infraestructure regression | 820 passed, 37 Windows discovery skips |
| Desktop regression | 422 passed, zero skips |
| Sequential owning coverage | The same 795 / 820 + 37 skips / 422 cases pass in separate runs |
| Assembly restoration | All 15 PSPMan application/test/dependency DLL copies under Debug output match their pre-coverage SHA-256 after each collector exits |

Skipped theories may represent several unexecuted scenarios; 37 skips do not
mean 37 passing Windows tests or define the complete Windows/physical matrix.
Required-native guards make missing native assets fail instead of silently
skipping available Linux adapter/bridge proof. Native Windows guards and runs
remain separate mandatory work in [pending validation](./pending-validation.md).

## Sequential owning-component coverage

Each command ran to completion before the next, with no application or other
suite using instrumented binaries. The collector configuration requests JSON
and Cobertura, without excluding Windows sources or inventing a numeric target:

```sh
dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --no-build --no-restore --collect 'XPlat Code Coverage' --settings <json-cobertura.runsettings> --logger trx --results-directory <scratch>/coverage-core
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --no-build --no-restore --collect 'XPlat Code Coverage' --settings <json-cobertura.runsettings> --logger trx --results-directory <scratch>/coverage-infra
dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --no-build --no-restore --collect 'XPlat Code Coverage' --settings <json-cobertura.runsettings> --logger trx --results-directory <scratch>/coverage-desktop
```

The runsettings DataCollector is `XPlat Code Coverage`, with
`<Configuration><Format>json,cobertura</Format></Configuration>`.
Assembly SHA-256 baselines include the application/test assemblies and their
PSPMan dependency copies, checked after each run. Rebuild before subsequent
application/native publication checks. Reports are managed IL/source coverage,
not C++ 7-Zip/bridge coverage, XAML rendering coverage or physical acceptance.

Use only the package named for the owning assembly from its owning suite's
Cobertura report. Dependency hits from Desktop's native-close tests are not
merged into Infrastructure or Core coverage, and percentages are not averaged.

| Owning assembly / suite | Line coverage | Branch coverage |
| --- | ---: | ---: |
| PSPMan.Core / Core.Tests | 96.64% | 89.78% |
| PSPMan.Infraestructure / Infraestructure.Tests | 71.38% | 60.32% |
| PSPMan.Desktop / Desktop.Tests | 94.70% | 89.73% |

### Transfer and composition detail

Source figures use distinct line numbers within each source, combining hits
across its classes/state machines. Branches are the owning JSON report's
class/method/line/offset/path identities; no cross-suite branch union is made.
“No branches” means the collector reports no branch denominator.

| Owner / source | Covered lines | Covered branches |
| --- | ---: | ---: |
| Core / GameCopyModels | 39/39 (100%) | No branches |
| Core / GameCopyRules | 108/109 (99.08%) | 148/156 (94.87%) |
| Core / GameCopyService | 241/245 (98.37%) | 131/156 (83.97%) |
| Core / GameCopySessionCoordinator | 293/304 (96.38%) | 219/261 (83.91%) |
| Core / GameCopySubmissionRules | 69/69 (100%) | 63/70 (90.00%) |
| Core / GameCopySpaceReservations | 66/66 (100%) | 36/38 (94.74%) |
| Infrastructure / ArchiveRuntimeResolver | 53/54 (98.15%) | 43/60 (71.67%) |
| Infrastructure / ArchiveSizeInspector | 13/21 (61.90%) | 4/4 (100%) |
| Infrastructure / SevenZipGameArchiveReader | 9/9 (100%) | No branches |
| Infrastructure / SevenZipNativeSession | 317/325 (97.54%) | 122/163 (74.85%) |
| Infrastructure / CopyDestinationResources | 368/393 (93.64%) | 160/197 (81.22%) |
| Infrastructure / CopyAccessStream | 63/70 (90.00%) | 35/40 (87.50%) |
| Infrastructure / FileGameCopyFileSystem | 26/30 (86.67%) | 5/8 (62.50%) |
| Infrastructure / LinuxArchiveSource | 58/68 (85.29%) | 20/30 (66.67%) |
| Infrastructure / LinuxCopyDestination | 171/178 (96.07%) | 116/134 (86.57%) |
| Infrastructure / LinuxCopyFileAccess | 170/180 (94.44%) | 108/138 (78.26%) |
| Infrastructure / WindowsArchiveSource | 0/65 (0%) | 0/30 (0%) |
| Infrastructure / WindowsCopyDestination | 0/161 (0%) | 0/108 (0%) |
| Infrastructure / WindowsCopyFileAccess | 0/242 (0%) | 0/178 (0%) |
| Infrastructure / WindowsCopyNative | 17/133 (12.78%) | 40/126 (31.75%) |
| Infrastructure / WindowsCopyPathRules | 37/38 (97.37%) | 90/94 (95.74%) |
| Desktop / CopyOperationViewModel | 126/126 (100%) | 126/130 (96.92%) |
| Desktop / CopySubmissionViewModel | 107/107 (100%) | 83/90 (92.22%) |
| Desktop / GameCopyViewModel.Operations | 71/72 (98.61%) | 58/74 (78.38%) |
| Desktop / GameCopyViewModel.Shutdown | 52/53 (98.11%) | 41/50 (82.00%) |
| Desktop / GameCopyViewModel internal historical adapter | 123/130 (94.62%) | 65/74 (87.84%) |
| Desktop / CopyCloseConfirmationViewModel | 11/11 (100%) | No branches |
| Desktop / CopyInputViewModel | 19/19 (100%) | No branches |
| Desktop / MainViewModel.Copy | 19/19 (100%) | 12/12 (100%) |
| Desktop / MainViewModel.CopySubmissions | 81/87 (93.10%) | 62/70 (88.57%) |
| Desktop / CopyStartup | 20/20 (100%) | 11/12 (91.67%) |
| Desktop / ApplicationDeviceLifetime | 142/144 (98.61%) | 51/56 (91.07%) |
| Desktop / CopySelectionModeConverter | 1/2 (50%) | 4/4 (100%) |

### Significant gaps and limits

- Windows native source/destination paths are entirely unexecuted. Portable
  Windows path/identity logic accounts for WindowsCopyNative hits, not Windows
  system-call, driver, sharing, UAC, FAT or physical device evidence. Windows
  device-provider paths also contribute to the lower Infrastructure total and
  remain included in its denominator.
- Core service gaps include invalid archive identities, incomplete extraction,
  unexpected callbacks and progress guards. Registry gaps include cancellation
  callback exceptions/completion races, exceptional close/dispose drains and
  invalid-root rejection in the retained single-call adapter. Submission and
  reservation branch gaps are defensive combinations; passing overlap/race
  tests do not exhaust every possible interleaving.
- Shared native resources cover real capacity, waits, settlement, skips, retry,
  partial writes and owner faults in temporary roots. Remaining lines include
  invalid operation/native binding, changed root identity, exceptional ledger
  guards and synchronous/unsupported accounted-stream members. CopyAccessStream
  and ArchiveSizeInspector retain unsupported members and exceptional counter/
  release paths. Real multi-exabyte output is not attempted to cover overflow.
- Native loader/session gaps include ABI/load-failure cleanup, oversized/invalid
  metadata and uncommon callback/error paths. Linux gaps include kernel/native
  failures, link/root race branches and unsupported source-stream operations.
  Native software tests with injected inventory/fault scheduling do not prove
  actual USB loss, removable filesystem behavior, full media or authorization.
- Desktop gaps include defensive unknown-operation routing, admission-exception
  mapping, generic historical-adapter failures and initialization/stop/decision
  exception barriers. Row/input stale/disposed/closing conditions have branch
  gaps despite actual cancellation, removal and aggregate-close race coverage.
  The one-way selection converter's unsupported ConvertBack is unexecuted.
- No report certifies physical PSP/source media, multiple real optical readers,
  Windows rendering/native prompts, OS logout, forced termination or every
  deployment architecture. A passing graphical/managed probe cannot close those
  gates, and coverage percentages are not an acceptance verdict.

## Native runtime/package checks

Current staged inventory contains only `linux-x64`. Both binaries are ELF x86-64
shared objects. ABI 1, engine version 2606 (7-Zip 26.06) and the pinned source
SHA-256 are recorded in the manifest and exercised by native resolver/reader
checks. All **17 staged files** were independently rehashed with matching
results, including engine, bridge, full source archive, notices and bridge/build
sources. Packaged bridge/build sources match current repository inputs byte for
byte. This establishes consistency with pinned inputs, not independent signature
verification of an arbitrary runtime.

- Source: `a4e374c8f864f51df48c2cfd0e1413ec3b2ea030deae852d6a655402ebb61ac4`.
- Engine: `eecf6e133a35f4e857a821fd80738a2636c0dff2fb704a3dab286ec8ee6575c3`.
- Bridge: `6059ea325114313dc20030810d668bf773e98756fd17bc023fc14f283e184cd6`.

For each `runtime.json` Files entry, compute the file's SHA-256 relative to its
native package directory and compare it with the entry; also compare the full
source archive hash with SourceSha256. Compare each bridge-source file with its
matching repository input. Reapply these checks to publication output, not only
the staged source package. Tests using PSPMAN_TEST_PUBLISH_ROOT must resolve the
published runtime explicitly rather than accidentally using repository assets.

After the successful post-coverage root build, local publication passes:

```sh
dotnet publish src/PSPMan.Desktop/PSPMan.Desktop.csproj -c Release -r linux-x64 --self-contained false -o <scratch>/publish-linux
PSPMAN_REQUIRE_NATIVE_TESTS=1 PSPMAN_TEST_PUBLISH_ROOT=<scratch>/publish-linux dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~GameArchiveReader|FullyQualifiedName~GameArchiveConcurrencyTests|FullyQualifiedName~GameCopyServiceIntegrationTests|FullyQualifiedName~GameCopyEngineResourceTests' --blame-hang-timeout 60s --blame-hang-dump-type mini --logger trx --results-directory <scratch>/published-native
```

The published runtime's 17 assets/source copies match before and after the
native run: **109 passed, zero failures, two Windows discovery skips**. This
includes actual native reader/session overlap and shared engine resources
against the publication, with fixture source/output hash and release assertions.
The two Windows theories remain unexecuted, not accepted by the Linux results.

Publication with `--no-build --no-restore` and
`-p:PspArchivePackageRoot=<scratch>/absent-runtime` is rejected with
“Build and package the matched 7-Zip/PSPMan archive runtime for linux-x64 before
publishing.” This is the expected negative package check, not a failed positive
regression. No staged asset is removed and no publication is distributed.

## Controlled graphical verification

Both graphical runs occur after coverage restoration/rebuild and publication
tests. Each uses a temporary catalogue/config/cache and a private D-Bus, with
DBUS_SYSTEM_BUS_ADDRESS redirected to that bus and ListActivatableNames checked
to exclude org.freedesktop.UDisks2. No arbitrary hardware discovery/mount or
physical game transfer is performed.

1. Production starts through `dotnet run --project
   src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build --no-restore`. Only its
   owned PID/window is observed. The isolated SQLite catalogue is created;
   geometry is observed at 1200 × 650 and then **900 × 450**. An own-window X11
   capture is inspected: empty catalogue, English controls, Prepare copies
   disabled and the expected visible private-bus device-service error. A normal
   window-close request exits **0** with no unhandled application exception.
   No focus change or keyboard input is sent to other applications.
2. A temporary native X11 harness reuses the existing shutdown fixture, actual
   Avalonia views and bound buttons with controlled inventory/byte I/O. Two
   active rows and a pending draft are created. Rendered 900 × 450 window
   captures are inspected; both aggregate choices fit after temporary draft
   hiding. Decline preserves both owners/draft and reopens admission. An old
   confirmation cannot accept a fresh decision. Acceptance drains both owners,
   retains both one-byte outputs, disposes the provider once and exits **0**.
   Keyboard behavior is covered separately by the headless regression; the
   native harness invokes the bound buttons directly.

Private-bus portal/FUSE/keyring diagnostics are environment messages, not
physical authorization evidence. The controlled graphical harness is distinct
from the four actual Linux native archive/filesystem callback cases in Desktop
regression. Neither substitutes for Windows graphics, real device presentation,
native authorization/removal or OS logout. Temporary harnesses, TRX, coverage
files, screenshots and publication outputs are verification intermediates; this
report records their reproducible scope/results rather than treating them as
portable hardware evidence.

## Completion status

The [requirement/AC index and physical procedures](./manual-validation.md) cover
FR-1–FR-24, NFR-1–NFR-4 and AC-1–AC-24, with P01–P26 unexecuted on both physical
platforms. All inherited Windows and Spec 004 native gates remain open.
Available verification in this approved partial continuation is complete; the
mandatory physical/native remainder is not. Specification 0.6 stays approved;
T10 is unchecked. No production/test source, packages or device policy changes,
commit or push occur. Final document links/identifiers and added-line whitespace
are checked. This is not formal SDD validation/code review or feature acceptance.
