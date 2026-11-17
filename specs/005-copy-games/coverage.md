# T10 — Regression, coverage and runtime evidence

Evidence date: 2026-11-17. Specification 0.5; plan/tasks 0.2. Task consolidation authorized by the user's
“apruebo el plan”. Baseline: clean local dev `eb28eb6`; branch
`feature/005_T10_regression-coverage-native-evidence`. No production/test code,
packages, specification requirements or device policy changed. Existing behavior
tests already exercise this documentary task's evidence; no artificial RED test
or implementation change was introduced. T10 and whole-feature verification
remain incomplete pending mandatory Windows/physical evidence.

## Current automated checks

Linux x86_64, .NET SDK 10.0.404, xUnit 2.9.3/VSTest with
Microsoft.NET.Test.Sdk 17.14.1 and coverlet.collector 6.0.4. No global.json,
Directory.Build.props or Directory.Packages.props overrides were found.

| Check | Actual result |
| --- | --- |
| `dotnet build PSPMan.slnx` | PASS before collection and again after it; zero warnings/errors |
| `dotnet test PSPMan.slnx --logger trx --results-directory <scratch>/t10-regression` | PASS; 1850 passed, zero failed, 26 Windows discovery entries skipped |
| Core regression | 734 passed, no skips |
| Infraestructure regression | 739 passed, 26 Windows skips |
| Desktop regression | 377 passed, no skips |

Skipped theories may represent multiple unexecuted fixture rows. The 26 are not
26 passing Windows scenarios and are not a complete hardware matrix. Native
Windows bridge/reader tests also need a real Windows runner even where their
Linux counterpart passes. See [pending gates](./pending-validation.md).

## Sequential owning-component coverage

Run these commands separately, waiting for each process to exit before the next;
no application/test process may use the instrumented binaries concurrently:

```sh
dotnet test tests/PSPMan.Core.Tests/PSPMan.Core.Tests.csproj --no-build --collect:"XPlat Code Coverage" --results-directory <scratch>/t10-coverage-core
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --no-build --collect:"XPlat Code Coverage" --results-directory <scratch>/t10-coverage-infra
dotnet test tests/PSPMan.Desktop.Tests/PSPMan.Desktop.Tests.csproj --no-build --collect:"XPlat Code Coverage" --results-directory <scratch>/t10-coverage-desktop
```

Each collector produces a `coverage.cobertura.xml` attachment. Read the package
matching its owning test project, rather than adding duplicate dependency
assemblies from other projects or averaging their percentages. Results measure
managed IL/source, **not** C++ native bridge/engine coverage or XAML rendering.
No numeric acceptance threshold has been invented. Rebuild before later native
application runs. Scratch TRX/XML files are intermediate results, not portable
physical evidence; this report records the commands, scope and observed values.

| Owning assembly | Line coverage | Branch coverage |
| --- | ---: | ---: |
| Core, measured by Core.Tests | 96.45% | 89.31% |
| Infraestructure, measured by Infraestructure.Tests | 70.09% | 59.28% |
| Desktop, measured by Desktop.Tests | 93.98% | 89.50% |

### Transfer component detail

Per-file figures below use distinct source lines across compiler-generated
classes/state machines, taking maximum hits and reported covered/total branch
counts per source line. They are a source view, not a cross-suite branch union.
“No branches” means the collector reports no branch denominator.

| Owner / source | Lines | Branches |
| --- | ---: | ---: |
| Core / GameCopyRules | 108/109 (99.08%) | 148/156 (94.87%) |
| Core / GameCopyService | 213/220 (96.82%) | 120/140 (85.71%) |
| Core / GameCopySessionCoordinator | 239/244 (97.95%) | 200/245 (81.63%) |
| Core / GameCopyModels | 33/33 (100%) | No branches |
| Infraestructure / ArchiveRuntimeResolver | 53/54 (98.15%) | 43/60 (71.67%) |
| Infraestructure / ArchiveSizeInspector | 13/21 (61.90%) | 4/4 (100%) |
| Infraestructure / SevenZipNativeSession | 316/325 (97.23%) | 121/163 (74.23%) |
| Infraestructure / SevenZipGameArchiveReader | 9/9 (100%) | No branches |
| Infraestructure / CopyAccessStream | 60/68 (88.24%) | 30/36 (83.33%) |
| Infraestructure / FileGameCopyFileSystem | 26/30 (86.67%) | 1/2 (50%) |
| Infraestructure / LinuxArchiveSource | 58/68 (85.29%) | 16/26 (61.54%) |
| Infraestructure / LinuxCopyDestination | 120/127 (94.49%) | 52/62 (83.87%) |
| Infraestructure / LinuxCopyFileAccess | 169/179 (94.41%) | 91/118 (77.12%) |
| Infraestructure / LinuxCopyNative | 40/45 (88.89%) | 25/38 (65.79%) |
| Infraestructure / WindowsCopyPathRules | 37/38 (97.37%) | 90/94 (95.74%) |
| Infraestructure / WindowsArchiveSource | 0/65 | 0/26 |
| Infraestructure / WindowsCopyDestination | 0/109 | 0/32 |
| Infraestructure / WindowsCopyFileAccess | 0/240 | 0/166 |
| Infraestructure / WindowsCopyNative | 17/130 (13.08%) | 40/124 (32.26%) |
| Desktop / GameCopyViewModel | 164/171 (95.91%) | 88/100 (88.00%) |
| Desktop / GameCopyViewModel.Shutdown | 34/34 (100%) | 23/26 (88.46%) |
| Desktop / CopyCloseConfirmationViewModel | 11/11 (100%) | No branches |
| Desktop / CopyInputViewModel | 19/19 (100%) | No branches |
| Desktop / MainViewModel.Copy | 17/17 (100%) | 10/12 (83.33%) |
| Desktop / CopyStartup | 16/19 (84.21%) | 9/10 (90.00%) |
| Desktop / ApplicationDeviceLifetime | 128/130 (98.46%) | 47/52 (90.38%) |

### Significant gaps and interpretation

- Windows native source/destination/access paths are unexecuted. Portable
  name/identity calculations account for some WindowsCopyNative hits, without
  proving Windows system calls, filesystem sharing, volume replacement or FAT
  semantics. The low infrastructure total includes these and existing device
  provider native paths; none is excluded to improve the percentage.
- Core's remaining service lines include invalid reader entry identity, invalid
  callback/skip shapes and defensive source/session checks. The coordinator's
  remaining lines include final-publication cancellation/invalidation races,
  invalid selected-root resolution and exceptional drain completion. Unit/UI
  races that do pass do not establish every defensive interleaving.
- ArchiveSizeInspector's missing lines are unsupported stream members and the
  checked counter overflow handler; a real multi-exabyte decompression was not
  attempted. Core sum overflow is tested separately and does not cover that
  adapter catch. Native-session gaps include loader/ABI failures after verified
  resolution and impossible-sized metadata/callback defenses. Missing coverage
  is disclosed, not treated as automatically safe or passed.
- Linux confinement uses actual openat2/statx temporary-root integration, with
  injected inventories/faults. Kernel/filesystem fallback/error paths, removable
  FAT behavior, privilege delays, physical loss and disk-full/flush effects still
  need designated-media tests. Native archive callback tests exercise actual
  7-Zip, but are not a hardware-removal demonstration.
- Desktop headless tests exercise compiled bindings and lifetime/UI projection.
  The remaining copy VM lines include exceptional start/cancellation handling;
  CopyStartup's inventory callback is not invoked by controlled-I/O composition
  tests. ApplicationDeviceLifetime's remaining lines 59/125 are defensive
  initialization/stop exception barriers. These are disclosed gaps rather than
  additional exercised production-adapter flows.
  They do not prove Windows rendering, native device prompts, OS logout, forced
  termination behavior or physical PSP copying. Historical controlled X11
  evidence remains scoped to [T08](./copy-screen-interactions.md) and
  [T09](./confirmed-shutdown.md).

## Runtime/package and graphical checks

Only `linux-x64` is staged. Desktop declares no RuntimeIdentifiers or publish
profiles, so this is the verified target available here, not a declaration that
Linux/Windows support is restricted to x64. Windows, other CPUs/libcs and actual
deployment-target inventory remain mandatory open work.

The staged manifest reports ABI 1, version 2606 (7-Zip 26.06), RID linux-x64 and
source SHA-256
`a4e374c8f864f51df48c2cfd0e1413ec3b2ea030deae852d6a655402ebb61ac4`.
All 17 listed assets were independently rehashed and matched, including the full
pinned source archive, bridge/build sources and GPL/LGPL/unRAR notices. Both
binaries are ELF x86-64 shared objects. The manifest's engine hash is
`eecf6e133a35f4e857a821fd80738a2636c0dff2fb704a3dab286ec8ee6575c3`;
bridge hash is
`6059ea325114313dc20030810d668bf773e98756fd17bc023fc14f283e184cd6`.
This verifies consistency against the pinned packaged inputs, not an independent
signature/authentication of an arbitrary engine. The required-native Linux test
run passed 739 with the same 26 Windows skips; ABI/version exports and
missing/mismatched-package rejection are covered by existing tests.

The packaged bridge/build source files also compare byte-for-byte with current
repository inputs. The local publication and reader/service checks passed:

```sh
dotnet publish src/PSPMan.Desktop/PSPMan.Desktop.csproj -c Release -r linux-x64 --self-contained false -o <scratch>/t10-publish-linux
PSPMAN_REQUIRE_NATIVE_TESTS=1 PSPMAN_TEST_PUBLISH_ROOT=<scratch>/t10-publish-linux dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --no-build --filter 'FullyQualifiedName~GameArchiveReader|FullyQualifiedName~GameCopyServiceIntegrationTests' --logger trx --results-directory <scratch>/t10-published-reader
```

Publication succeeded and all 17 published manifest assets were rehashed with
matching results. Reader/service execution against the published runtime passed
66 cases, with one Windows theory skipped. This is actual application-local
native extraction, not reference CLI or historical managed-reader evidence.
Publication with `--no-build --no-restore` and
`-p:PspArchivePackageRoot=<scratch>/t10-absent-runtime` failed as expected with
exit 1 and “Build and package the matched 7-Zip/PSPMan archive runtime for
linux-x64 before publishing.” This intentional negative check is not a failed
regression. No published binary was distributed or deployed.

After coverage/rebuild, a fresh graphical smoke used
`dotnet run --project src/PSPMan.Desktop/PSPMan.Desktop.csproj --no-build --no-restore`
inside `dbus-run-session`, with temporary XDG data/config/cache roots and
`DBUS_SYSTEM_BUS_ADDRESS` set to that private session bus. Before startup,
ListActivatableNames was checked to exclude org.freedesktop.UDisks2. Only the
launched application's PID/window was observed and controlled. Its temporary
SQLite catalogue was created; the 1200 × 650 window rendered and was resized to
900 × 450. The own-window capture was inspected: empty catalogue, Copy to PSP,
English controls and the expected visible device-unavailable error from the
isolated bus. Alt+F4 with no active copy exited 0 without an unhandled exception.
Private-bus portal/FUSE/keyring diagnostics occurred; no UDisks discovery,
physical mount, archive/PSP transfer or active-copy close was exercised by this
smoke. Active-copy controlled-I/O UI evidence remains T09, and physical matrix
rows remain open.

## Completion status

[The requirement/AC index and physical procedures](./manual-validation.md) cover
FR-1–FR-19, NFR-1–NFR-4 and AC-1–AC-19. Current automated evidence is passing;
mandatory Windows, real PSP/source media, actual filesystem/native removal and
OS-shutdown checks remain unexecuted. T01/T03/T05/T06 and T10 remain open.
Specification 0.5 remains approved, not implemented. Formal SDD review and user
acceptance have not been performed or authorized by this consolidation.
