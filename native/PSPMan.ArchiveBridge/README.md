# PSPMan archive bridge — T01 compatibility gate

This is the minimal native API feasibility increment for approved Spec 005,
plan/tasks 0.2. It is **not** the production archive adapter or a deployed copy
feature. Core, Desktop and actual PSP volumes are untouched. T02/T03 define the
production contracts, runtime resolver, source confinement and distribution.

## Pinned inputs and licences

[source.lock.json](./source.lock.json) pins the full official 7-Zip **26.06**
source archive, SHA-256 and original release URL. Verify the downloaded archive
before extracting it; do not substitute the smaller LZMA SDK or an installed
7zz executable. Build `CPP/7zip/Bundles/Format7zF` without editing upstream code.
Only ZIP, RAR4 and RAR5 handler IDs are selectable through this bridge.

The new bridge source follows the repository GPL-3.0 licence. Upstream headers
and the separate full engine retain their own LGPL-2.1-or-later, unRAR and BSD
terms: see [licences](./licenses/) and the unmodified source archive. Runtime
output includes the upstream notices. Keep the exact source archive/build
instructions and required notices available with any later distributed binary;
T03 must implement and verify publish packaging/source availability. This gate
does not publish binaries. The ignored `out/` directory contains build outputs,
not repository or user input files.

## Linux build

Requires a C/C++17 toolchain, GNU make, pthread/dl and target C/C++ runtime.
Nothing is downloaded or installed by the build script. Verify and extract the
source archive in a dedicated build directory, then from the repository root:

```sh
sha256sum /path/to/7z2606-src.tar.xz
# Expected: a4e374c8f864f51df48c2cfd0e1413ec3b2ea030deae852d6a655402ebb61ac4
bash native/PSPMan.ArchiveBridge/build-linux.sh /path/to/extracted-source
PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter 'FullyQualifiedName~NativeArchiveBridge'
```

`CC` and `CXX` can select compiler wrappers; `BUILD_JOBS` defaults to 2. The
bridge builds with warnings treated as errors. Upstream uses its own make flags
with `-Wall -Wextra`; warnings are not made fatal for unmodified third-party
source. For a portable Zig compiler, wrappers must invoke `zig cc "$@"` and
`zig c++ "$@"` respectively. This task used official Zig 0.15.2 Linux x64,
archive SHA-256 `02aa270f183da276e5b5920b1dac44a63f1a49e55050ebde3aecc9eb82f93239`,
verified against the official download index. Zig is a development tool only;
there is no Zig application dependency or automatic tool installation.

## Windows build and pending execution

Use a Visual Studio C++ developer shell for the target architecture, including
Windows SDK and nmake. PowerShell:

```powershell
Get-FileHash C:/build/7z2606-src.tar.xz -Algorithm SHA256
./native/PSPMan.ArchiveBridge/build-windows.ps1 -SourceRoot C:/build/7zip-source -Platform x64
$env:PSPMAN_REQUIRE_NATIVE_TESTS = '1'
dotnet test tests/PSPMan.Infraestructure.Tests/PSPMan.Infraestructure.Tests.csproj --filter 'FullyQualifiedName~NativeArchiveBridge'
```

Windows build/runtime/callback tests have **not** been executed in this task's
Linux environment. Their success is a mandatory T01 gate, not inferred from
Linux. The current Desktop project has no declared RuntimeIdentifiers or publish
profiles; this task does not invent a narrower supported architecture list.
Reconcile actual Linux/Windows deployment architectures during T03 and test each
published target. Windows scripts above remain unverified until native execution.

## ABI and ownership

[bridge.h](./bridge.h) defines ABI 1. Fixed-width scalars and UTF-8 byte strings
cross the C boundary; native COM interfaces and platform wchar_t do not. The
engine uses UTF-16 units in BSTR/UString even in Linux 32-bit wchar_t; conversion
preserves surrogate pairs and explicit metadata string lengths. Invalid Unicode
returns an error instead of silently substituting another path.

A session accepts read/seek/open/close callbacks for caller-owned source handles,
a transient password callback and indexed begin/write/end callbacks. The bridge
never receives a PSP destination directory and never calls an extraction-to-path
API. Metadata exposes directory/link/encryption/size flags; the later Core policy
must decide unsafe/link/name handling before supplying an output stream.

Begin returns skip to route decoded bytes to a discard sink. Duplicate filenames
retain distinct indices. Extraction can select sorted indices; the engine replays
solid dependencies without calling destination callbacks for completed indices.
Decoded and accepted written bytes are separate counters, serialized together
with progress callbacks. Native engine progress is used for cancellation checks,
not blindly interpreted as copied bytes. There is no whole-game native buffer.

Only cancellation is safe concurrently with a session call. It signals an atomic
flag; read/write/progress/password paths cooperate. Caller-owned blocked I/O must
also be released/cancelled by its provider: signalling cannot forcibly terminate
an arbitrary managed callback. Await native return before destroying the session,
closing handles or releasing delegates. Callback exceptions must be caught on
the managed side and mapped to an error; no exception may cross the ABI. The
probe retains the original managed cause and all partial output bytes.

Data/CRC failures on encrypted entries map to indeterminate password/damage when
no definite cause exists. A failed invocation stops and closes current outputs.
Retry in this gate uses a newly opened session; the later service owns explicit
input decisions, retained output, overwrite consent and operation identity.
Definite wrong-password results, missing input, cancellation, corruption and
callback I/O have distinct results. The version export describes the **bridge's
pinned source version**; it does not authenticate an arbitrary engine DLL. T03
must bind packaged bridge/engine hashes/version/runtime and reject mismatches.

## Test scope

The managed probe loads only explicit test-local absolute paths, by default the
build outputs next to this file. Optional `PSPMAN_TEST_NATIVE_BRIDGE` and
`PSPMAN_TEST_NATIVE_ENGINE` override those paths for native CI. They are test-only,
not production discovery or fallback. `PSPMAN_REQUIRE_NATIVE_TESTS=1` makes missing
assets fail. Without built assets, native facts/theories are explicitly skipped,
and the mandatory gate remains pending. A skipped theory is one discovered case,
not evidence that its individual fixture rows passed.

Fixtures are small licensed files under the infrastructure test project; the
probe may buffer those payloads to compare independent hashes. This is not a
production buffering design. No archived executable is executed. The test-local
part resolver normalizes the known corpus's `.partNN.rar`, `.rNN` and `.zNN`
selections to their first part in the same fixture directory; callbacks reject
non-leaf requests. Production discovery and identity-bound handle access belong
to T03/T04/T05 and cannot be inferred from this fixture-only helper.

See [native gate evidence](../../specs/005-copy-games/native-archive-compatibility.md)
for actual results, regressions and pending Windows/physical checks.

### Native contract harness

Build and run the standalone harness with the same compiler/source tree:

```sh
c++ -std=c++17 -O2 -Wall -Wextra -Werror -I /path/to/extracted-source \
  native/PSPMan.ArchiveBridge/bridge-contract-tests.cpp \
  /path/to/extracted-source/CPP/Common/MyWindows.cpp -ldl -lpthread \
  -o native/PSPMan.ArchiveBridge/out/bridge-contract-tests
native/PSPMan.ArchiveBridge/out/bridge-contract-tests
```

It checks UTF-8/UTF-16 conversion (including supplementary characters, embedded
NUL metadata lengths and malformed UTF-8), two threads concurrently making 2,000
progress calls without overlapping managed-facing callbacks, and cancellation
without a late progress callback. Checks stay active in optimized builds.
Windows must compile/run its equivalent harness with cl and the same upstream
headers, linking oleaut32/uuid; that execution remains pending.
