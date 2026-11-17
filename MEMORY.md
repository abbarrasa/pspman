# Project memory

Durable context; see [AGENTS.md](./AGENTS.md) for standing instructions.

## Implemented state and workflow

- PSPMan implements catalogue CRUD, CSV flows, device selection, the single-game copy engine/screen and confirmed active-copy closure. Scanning is not implemented.
- Desktop composes SQLite at the OS local application-data path PSPMan/catalog.db. Corrupt startup/read state is reported without repair and disables catalogue mutations.
- [Spec 005](./specs/005-copy-games/spec.md) 0.5 and plan/tasks 0.2 are approved. T02/T04/T07/T08/T09 are complete in their scoped criteria. User explicitly approved T09's presented plan; [T09 evidence](./specs/005-copy-games/confirmed-shutdown.md) records checks/coverage/native limits. T01/T03/T05/T06 Windows and T10 physical gates remain open; Linux/fake tests never waive them.
- [T08 composition](./specs/005-copy-games/copy-screen-interactions.md) uses a caller-owned device session, typed one-row screen, actual-byte progress, transient input leases and retained-output warnings. Capture/reserve synchronously before queuing the coordinator runner; queuing the entire start allowed immediate selection changes to substitute the accepted game. Dispose copy before its device session.
- T09 coalesces close decisions; decline preserves copy/input/devices, accept drains copy before detachment/provider disposal without output cleanup. Pending confirmation blocks a new start even if copying finishes. Decision leases reject old commands. Reserve initialization/disposal tasks before callbacks to avoid reentrant competing cleanup or missed late owners; recheck stopping after construction/attachment. Programmatic disposal retires pending decisions unconditionally. Branch feature/005_T09_confirmed-copy-shutdown; no commits/push.
- T09 checks: build zero warnings/errors; regression 1850 passed/26 existing Windows skips (734/739/377). All 28 new shutdown and 46 focused lifetime/input cases pass. Shutdown VM coverage 100% lines/88.46% branches; owner 98.46%/90.38%. Native Avalonia/X11 at 900 × 450 with controlled I/O demonstrated decline/renewed confirmation/accepted drained exit 0; isolated production startup/normal close also passed. These do not prove real archive/PSP transfer or OS logout.
- T10 is not authorized/started. Present its scoped plan and await approval; “continue” requests planning first. Mandatory Windows/physical dependencies remain open. No SDD validation/acceptance is authorized.
- Spec 004 revision 0.3, plan/tasks 0.1: T01–T48 and AUD-1 complete; T49–T53 remain open for physical/Windows evidence. [Documentary consolidation](./specs/004-device-mounting/pending-validation.md#current-blockers-and-restart-order) retains gates/resume steps without provisioning or acceptance authority.

## Domain and persistence

- Identity is Medium plus ordinal case-sensitive Location, with normalized DVD Number only for DVD; edits preserve IDs. Location is medium-rooted slash syntax, not a host path, and validation never probes files.
- DVD needs positive Int32 number; zero normalizes to null. Non-DVD number is null; SizeBytes is positive Int64 decompressed size; Notes permit 2,500 UTF-16 code units.
- SQLite uses parameterized CRUD and separate case-sensitive DVD/non-DVD indexes. Existing catalogues are validated before transactional migration; malformed data fail without recovery. Later connections use ReadWrite to avoid recreating a missing catalogue.
- Microsoft.Data.Sqlite 10.0.13 and SQLitePCLRaw.bundle_e_sqlite3 2.1.14 avoid the older vulnerability warning. Save/import retain confirmed writes if refresh fails; corruption remains distinct.

## CSV contracts

- Schema: Title, Medium, DvdNumber, Location, Format, SizeBytes, Notes; labels Dvd/Usb/MemoryCard and ISO/CHD. Exports use integer bytes, never IDs or approximate sizes.
- Semicolon CSV uses strict UTF-8, optional import BOM/export BOM, quoting, escaped quotes and multiline Notes. Export protects formula-like text with a tab; import removes exactly one tab only before the first non-whitespace =, +, - or @.
- Headings are case-insensitive and reordered/extra columns work; all seven are required, last duplicate wins. Heading whitespace and whitespace in Medium/Format labels are rejected.
- Short rows fail; surplus cells and blank/spaces-tabs-only physical lines outside quotes are ignored. Errors identify logical-record start lines; fatal decoding/parsing/read failures discard the preview.
- Only the last valid row per identity remains; invalid later duplicates cannot replace it. Identical records (including null/empty Notes equivalence) cause no proposal/write; empty Notes clears text.
- Preview uses one immutable catalogue snapshot without writes. Confirmation consumes proposals once, preserves edited IDs, retains successful writes, continues ordinary save failures and stops on corruption/source errors.
- SHA-256 covers complete source bytes and held-handle/current-path evidence before writes and after EOF. Repeated hashing favors detection; sharing is not an absolute cross-platform lock. No fixed CSV size limit; buffering is bounded but preview memory is input-dependent.
- Unedited template yields three invalid rows; header-only yields no proposals/errors. Template saving reuses protected export validation and replacement consent without catalogue access; normal export resets template mode.

## Desktop and verification pitfalls

- Preview/apply run off UI and resume on its context; cancel discards late preview results, apply cannot be cancelled, and CRUD is guarded during evaluation/review/apply.
- Headless tests use Avalonia.Headless 12.1.4, FluentTheme and a nonparallel collection. Result-less async callbacks require HeadlessTestDispatcher.DispatchAsync or explicit Dispatch<bool>; bare Dispatch(async () => ...) can return Task<Task>. Import uses Dispatch<int>.
- The [headless audit](./specs/004-device-mounting/headless-audit.md) corrected unawaited callbacks, recycled-button assertions and a Core/UI publication race. Await visible UI projection, not only Core revision; capture retired commands rather than recycled buttons for stale-action checks.
- Never instrument coverage while another test/app uses shared binaries: it caused BadImageFormatException. Rebuild and run sequentially. Preserve mixed legacy line endings; check added-line whitespace.
- Archive probes must own/dispose multipart streams; convenience RarReader enumeration leaked a part on cancellation. Fixture hashes require documented CRLF normalization; corpus CP866 is not a universal encoding policy.
- Native 7-Zip UString/BSTR keeps UTF-16 surrogate units even in Linux 32-bit wchar_t; UTF-32 conversion loses supplementary names. Retain delegates/handles until native return after cooperative cancellation. Source tokens differ from inspection fingerprints; SharpCompress is test-only.
- Coverlet injects the standard netstandard facade into instrumented Core. Its architecture guard explicitly permits that exact assembly name alongside System references; it still rejects non-framework dependencies. Authorized in T48.
- Ordinary startup enables automatic mounting: never smoke-test against arbitrary hardware. Use a temporary catalogue and verified private D-Bus without UDisks activation. Historical [coverage](./specs/004-device-mounting/coverage.md) and [native procedures](./specs/004-device-mounting/pending-validation.md) remain scoped evidence, not hardware/Polkit/UAC acceptance.

## Device state, lifetime and safety

- Core owns session-only associations, serialized operations/authorization, whole-device removal, explicit retries, success-only remount suppression and stale-work rejection. Recognition needs provenance-backed Sony USB descriptors plus storage evidence, never labels/directories or VID/PID as unique identity.
- Linux uses pinned Tmds.DBus.Protocol 0.94.1/UDisks2, excludes internal disks and separates reader/card identity. Empty native options, no force/shell/credentials; native refresh/errors remain authoritative. Private-bus tests are not hardware proof.
- Windows uses SetupAPI/Configuration Manager, storage IOCTLs and GUID volumes. Storage-number pairs group current disks, not reconnection identity; hubs never merge devices. UNIQUEID-backed USB identity needs session uniqueness; device identity is not media identity.
- Windows generations track GUID/filesystem serial/availability/disconnect, not labels/roots. Register observers before inventory; callbacks coalesce reconciliation; disposal releases registrations/handles.
- Windows mounting reuses roots or assigns free D–Z letters after revalidation/policy checks. Removal uses ordinary lock/flush, fresh topology and exact-node PnP scope, never hubs/force. CONFIGRET 5 differs from Win32 AccessDenied 5. Optical ejection also requires fresh empty-media confirmation.
- Elevation uses bounded/versioned strict JSON, one fixed authenticated operation and restricted local pipes. Validate bidirectional native PID/creation/image/user/session/elevation evidence; generic dotnet.exe fails closed. ShellExecuteEx/runas follows classified privilege failure on a dedicated STA worker; cancellation cannot dismiss UAC, so retain prompt ownership and release late handles.
- ApplicationDeviceLifetime starts providers off UI on Opened, attaches initializing/ready/error state without replacing MainViewModel and forwards latest game selection. Normal device shutdown awaits construction, subscriptions and operations before controlled exit; duplicate requests share cleanup, late providers cannot attach, cleanup failure exits 1. Forced termination is not graceful-shutdown evidence.
- DevicesViewModel observes caller-owned sessions, marshals immutable projections and revalidates association actions. No fallback, catalogue writes or game-file I/O. Typed views keep roots/errors/actions and a bounded 900 × 450 workspace; ViewLocator preserves namespaces.
- The copy coordinator owns one active slot, captured associations, relevant-change failure and removal reservations through file release/session acceptance. Removal interception stops matching copies before forwarding that same freshly revalidated device. Cancel/failure retains output without deletion/restoration; old prompt/progress responses cannot restart retired work.
