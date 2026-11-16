# T03 — Native archive adapter and runtime packaging

Status: Linux implementation verified; mandatory Windows gate open.
Specification 0.5; plan/tasks 0.2. The user's “Continúa con la siguiente tarea”
authorized T03 after T02, retaining the explicit Windows deferral.
Branch: feature/005_T03_native-archive-adapter. No commits or push.

## Implemented scope

Production uses SevenZipGameArchiveReader and an application-local 7-Zip 26.06
engine/C ABI bridge. SharpCompress is now a test-only historical dependency.
Runtime resolution validates the process RID, ABI/version, pinned source hash
and all 17 manifest assets; missing, changed or linked assets fail explicitly.
There is no PATH lookup or alternative backend. Packaging includes the official
source archive, notices and bridge/build/packaging source. SHA checks establish
bundle consistency, not authenticity against a privileged bundle replacement.

Sessions preserve indexed order, directories, links, duplicate and Unicode
names; normalize multipart selection; support encrypted headers and transient
password input; and stream selected outputs through caller-owned callbacks.
Cancellation and disposal await native return before releasing delegates/handles.
Callback failures preserve their original cause; output close errors take
priority. Reentrant session calls from its own callbacks fail explicitly.
FR-19 remains “Incorrect password or damaged archive”; retry reopens a session.
No PSP path is passed to the engine and no destination cleanup is introduced.

Core's source contract now captures an opaque evidence token before validation;
the inspection fingerprint also covers ordered part hashes and is not that token.
Cancelled is a typed archive callback outcome. These are technical refinements
within the approved contract scope, with no product requirement change.
Source parts are hashed during preparation and before/after extraction; this
bounded-memory validation costs additional compressed-source reads, especially
on optical media. Hashing and stream I/O honor cancellation.

Unknown sizes use a bounded discard/count pass, never SizeBytes or a full-game
buffer. Its test masks known metadata and exercises the real native count pass;
it does not claim a genuine unknown-size fixture. Test targets may buffer small
licensed fixture payloads; production does not buffer whole games.

## Verification

- `dotnet build PSPMan.slnx`: passed, zero warnings/errors.
- `PSPMAN_REQUIRE_NATIVE_TESTS=1 dotnet test PSPMan.slnx`: 1602 passed
  (648 Core / 642 Infrastructure / 312 Desktop), four existing Windows skips.
  Native archive tests execute rather than silently skip.
- 48 production adapter cases cover fixture bytes/hashes, multipart/password,
  solid replay/skips, corruption, retained partial output, pending cancellation,
  external disposal, forced GC, callback I/O/close errors, runtime rejection,
  package integrity, default application-local resolution and format signatures.
- Linux x64 framework-dependent Release publication succeeded. Adapter tests
  also run against its actual published native asset tree, without launching UI.
- Publishing with an absent package fails the explicit MSBuild validation gate.
- Shell syntax, editor diagnostics and added-line whitespace checks pass.

Initial absent-adapter cases failed; subsequent tests exposed split-ZIP format
selection and misleading extensions. The final detector prioritizes RAR magic
and permits split ZIP's final part without requiring its first bytes to be PK.

Windows native build, packaging, callback matrix and publication remain mandatory
and unexecuted; other CPU/libc targets are not verified. See
[pending validation](./pending-validation.md). No graphical startup, physical
PSP/source-media acceptance, filesystem confinement or copy UI is claimed.
