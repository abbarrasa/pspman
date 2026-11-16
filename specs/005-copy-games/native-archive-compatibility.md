# T01 — Native archive bridge compatibility evidence

Specification 0.5 and plan/tasks 0.2 are approved. The user explicitly authorized
resuming T01. Branch: `feature/005_T01_archive-compatibility`, the existing T01
branch based on local dev; no branch switch, commit or push was performed.

**Gate: open / not fully verified.** The Linux x64 native API gate passes.
Mandatory native Windows execution remains unavailable and unexecuted. T02 has
not started. This report does not establish a production copy feature, device
support or physical PSP acceptance.

## Implemented increment

- [Versioned C ABI and bridge](../../native/PSPMan.ArchiveBridge/bridge.h) over full
  official 7-Zip 26.06, using unmodified pinned source and dynamic engine loading.
  Only ZIP/RAR4/RAR5 handler IDs are selectable. No destination path is supplied
  to the native engine; output is delivered by indexed callbacks.
- Caller-owned source read/seek/part handles, transient password input, entry
  metadata/size/link flags, selected indices, skips/discard, decoded versus
  successfully written byte counters, cooperative cancellation and typed native
  results. Current outputs close before an extraction call returns on failure.
- [Test-local managed probe](../../tests/PSPMan.Infraestructure.Tests/NativeArchiveProbe.cs)
  with rooted delegates, guarded callbacks, retained managed I/O causes, partial
  output, exact close accounting and before/after hashes of all opened sources.
  No Core contract or production archive adapter is introduced in T01.
- [Build instructions and scripts](../../native/PSPMan.ArchiveBridge/README.md),
  source/release lock and unmodified upstream notices. Linux script execution
  passed. Windows script/exports are prepared but not executed; later runtime
  packaging, binary authentication and publish-target verification belong to T03.
- The previous SharpCompress dependency, tests and
  [blocked characterization report](./archive-compatibility.md) remain historical
  evidence. T03 owns production dependency replacement/test-only retention.

## Reproducible inputs and environment

Linux x64, Debian 13, .NET SDK 10.0.404; xUnit 2 / VSTest, no MTP runner setting or
central test-platform overrides. No host C++ compiler was installed. A temporary
portable compiler was downloaded from the [official Zig index](https://ziglang.org/download/index.json):
Zig 0.15.2 Linux x64, SHA-256
`02aa270f183da276e5b5920b1dac44a63f1a49e55050ebde3aecc9eb82f93239`.
It is a build tool, not an application dependency; no system packages changed.

Full official 7-Zip source archive:
`a4e374c8f864f51df48c2cfd0e1413ec3b2ea030deae852d6a655402ebb61ac4`.
See [source lock](../../native/PSPMan.ArchiveBridge/source.lock.json) for URLs and
version. The engine was built with its Format7zF makefile, no upstream edits or
assembly dependency; bridge/harness used C++17, `-O2 -Wall -Wextra -Werror`.
The bridge compiled without warnings. See the native README for portable compiler
wrappers and Linux/Windows commands. The standalone harness exits zero on success.

Observed Linux binary hashes (not portable reproducible-build guarantees):

| Output | SHA-256 |
| --- | --- |
| Full `7z.so` engine | `eecf6e133a35f4e857a821fd80738a2636c0dff2fb704a3dab286ec8ee6575c3` |
| `libpspman_archive_bridge.so` | `6059ea325114313dc20030810d668bf773e98756fd17bc023fc14f283e184cd6` |
| Standalone contract harness | `af98a765312eab227717e26fa12cc450ce3872649f8c8c75305156dc9d81f775` |

These ignored outputs are not distributed by the application. The bridge version
export pins its compiled headers; it does not authenticate an arbitrary loaded
engine. Matched assets, hashes, runtime architecture, source/notices availability
and publish outputs must be implemented/tested in T03. No native vulnerability
clearance is inferred from the earlier managed NuGet advisory audit.

## Tests-first and commands

Initial 16 compiled matrix rows failed with `NotImplementedException` from the
absent native probe: `native-bridge-red.trx`. A premature asset-required run also
failed while the bridge build was still finishing; no missing asset was accepted
as support evidence. An intermediate first real API run passed 37/41: it exposed
supplementary Unicode conversion and two incorrect fixture expectations. These
were corrected and the matrix was expanded; intermediate TRX files remain local
historical evidence, not final acceptance.

Executed from the repository root, with the built native assets:

```sh
# CC/CXX selected the temporary portable compiler wrappers documented in README.
bash native/PSPMan.ArchiveBridge/build-linux.sh /path/to/verified-7zip-source
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter 'FullyQualifiedName~NativeArchiveBridge' --logger 'trx;LogFileName=native-bridge-api-expanded.trx'
# Compile the standalone harness with the same headers/toolchain as documented.
native/PSPMan.ArchiveBridge/out/bridge-contract-tests
dotnet build PSPMan.slnx
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx --logger 'trx;LogFileName=t01-native-regression.trx'
bash -n native/PSPMan.ArchiveBridge/build-linux.sh
```

The expanded focused run passed 47/47. Two explicit ordered-part rows were then
added and passed in the final full regression: **49 native API cases, zero native
skips**. `PSPMAN_REQUIRE_NATIVE_TESTS=1` makes missing assets fail. On an unprepared
checkout native tests explicitly skip; a skipped theory is not its fixture rows
passing. Commands create TRX in each test project's ignored TestResults directory.

## Actual Linux API coverage

| Case | Actual result |
| --- | --- |
| Plain ZIP, ZIP64, AES deflate and AES LZMA | Matching independent payload hashes and actual metadata totals; the previously rejected LZMA fixture now passes. |
| PKWARE ZIP | Exact independent text `bla-bla-bla-bla-bla`, 19 bytes; this fixture contains one file, unlike the common three-file corpus. |
| Split ZIP, legacy multipart RAR, RAR5 multipart | Caller-owned part streams decode expected payloads; missing-part cases fail with result 6. |
| RAR4, RAR5, solid RAR | Matching payload hashes, metadata and size; indexed selection replays solid dependencies without duplicate output callbacks. |
| RAR4/RAR5 encrypted data and headers | Correct passwords decode; missing input returns password-required, and explicit reopen with correct input succeeds after failure. |
| Encrypted six-part RAR | All six parts decode correctly through actual bridge callbacks, overcoming the prior managed-backend rejection. |
| Modern part order | Distinct opened-part sequence is part01 through part06 for both RAR5 and encrypted RAR. |
| Non-first part selections | Test-local normalization of known `.part03.rar`, `.r02` and `.z01` names in the same fixture directory succeeds. This is not production discovery. |
| Wrong password versus damaged encrypted RAR4 | Both return indeterminate result 4, stop processing, end current outputs and retain decoded bytes; no definite cause or automatic retry is fabricated. |
| Definite wrong password | RAR5 encrypted-data and AES ZIP results stay distinct (3); encrypted-header ambiguity is retained where applicable. |
| Empty ZIP; early/late CRC damage | Empty metadata/no output; definite CRC damage returns 5 and stops at the failing entry, retaining prior/partial output. |
| Duplicate supplementary-Unicode paths | Distinct ordered indices and exact Unicode paths; skip-first and skip-all return no unwanted output, with decoded/written counters distinct. |
| Link entry | Metadata exposes the link; a controlled skip produces no output and no native link traversal. Core's later link/path policy is not implemented here. |
| Cancellation during ZIP/solid/multipart/encrypted multipart | Native call stops, current partial output remains, every opened source handle closes on session disposal. |
| External cancellation while a read callback is pending | Delegates survive forced GC; handles remain alive until the provider releases its callback/native returns, then all close. |
| Password callback cancellation | Returns cancellation (1), not password/damage ambiguity. |
| Source/destination callback I/O faults | Original managed IOException retained, result 2; source error not misclassified as password failure, partial destination output retained. |
| ABI/source version | ABI 1 and pinned source marker 2606 reported; callback ABI 99 rejected before opening an archive. |
| Native contract harness | Unicode round-trip, embedded-NUL length preservation and malformed UTF-8 rejection pass; two concurrent native callers deliver 2,000 serialized progress callbacks without overlap; cancel prevents late delivery. |
| Source integrity | Probe hashes every opened source before use and after complete disposal on success, cancellation and failure; no source changes observed. |

The common corpus's three payload hashes and 100,926-byte total are those in the
[fixture manifest/provenance](../../tests/PSPMan.Infraestructure.Tests/Fixtures/Transfers/README.md)
and historical report. Fixture executables are data only and are never executed.
Legacy non-UTF filename encoding remains native metadata behavior; the old corpus's
explicit managed CP866 setting is not adopted as a universal product policy.

## Regression and scope review

- Solution build: **passed**, zero warnings/errors.
- Solution tests: **1,524 passed, 4 skipped**: Core 618, Infraestructure 594,
  Desktop 312. The four skips are existing Windows native device tests. The
  infrastructure increase is the 49 native bridge cases; old 42 managed
  characterization cases remain. The pre-existing Core placeholder deletion is
  preserved, explaining 618 versus older 619 counts.
- Standalone native harness and shell syntax check: passed, exit 0.
- No new managed package or production Core/Desktop changes in resumed T01.
- No GUI launch, device discovery/mounting, native PSP file I/O, source catalogue
  mutation, commit or push. Existing local deletions remain untouched.
- This is T01 implementation evidence, not formal SDD code review, feature
  acceptance, measured feature coverage or completion of physical copy tests.

## Remaining gate and next step

Execute the [pending Windows native gate](./pending-validation.md) with a matching
C++/Windows SDK/.NET environment, actual callbacks and handle checks. Windows
build, standalone harness, ABI, archive matrix and regression remain mandatory.
All real-PSP copy/UI tests are later T10 work and remain unexecuted. Keep T01 open
for the pending Windows checks. The user subsequently explicitly deferred those
checks and authorized proceeding with T02's pure contracts/rules, overriding that
execution dependency for T02 only. This does not turn pending results into passes.
No new product decision or backend change is requested by these results.
