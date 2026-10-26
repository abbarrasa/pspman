# Project memory

Keep this file short (about 50 lines maximum). Update it when implementation changes make a note obsolete; remove stale details instead of appending a history log. See [AGENTS.md](./AGENTS.md) for standing instructions and verification commands.

## Current state

- PSPMan aims to catalog, manage, and deploy PSP game collections across removable media. The [README](./README.md) gives the product overview.
- The repository is still an initial scaffold. Core has game record/input types, pure rules, and catalog list/read/create/edit/delete use cases; SQLite can create and read catalogs, but writes, deployment, and removable-media handling are not implemented yet.
- [PSPMan.slnx](./PSPMan.slnx) contains Core, Infraestructure, Desktop, Core.Tests, and Infraestructure.Tests projects targeting .NET 10.
- [Core](./src/PSPMan.Core/) contains game record types, rules, and a repository contract/service with CRUD support alongside a placeholder `Class1`; [Infraestructure](./src/PSPMan.Infraestructure/) contains a SQLite read adapter and placeholder `Class1`; [Core.Tests](./tests/PSPMan.Core.Tests/) contains rule/service tests and an empty sample test; [Infraestructure.Tests](./tests/PSPMan.Infraestructure.Tests/) uses disposable SQLite catalogs.
- [Desktop](./src/PSPMan.Desktop/) is the Avalonia starter app. `App.axaml.cs` creates `MainWindow` with `MainViewModel`; the window displays a sample greeting.
- Infraestructure references Microsoft.Data.Sqlite 10.0.13 and SQLitePCLRaw.bundle_e_sqlite3 2.1.14; the explicit native-bundle version avoids a known vulnerability warning from the older transitive package. A new catalog gets a single GameRecords table; an existing catalog is checked without schema creation. Create/update/delete in the adapter currently throw NotSupportedException until their tasks.

## Details to remember

- `Infraestructure` is the existing project and directory spelling; keep it in paths and namespaces unless a rename is explicitly requested.
- [ViewLocator.cs](./src/PSPMan.Desktop/ViewLocator.cs) replaces `ViewModel` with `View` in the type's full name without changing the `ViewModels` namespace to `Views`. Check this mapping before relying on it for navigation.
- The repository-local [feature skill](./.agents/skills/feature/SKILL.md) requires a reviewable plan and user approval before changing files for a new feature.
- The skill-creator validator requires PyYAML; validate skills in an isolated Python environment when the system interpreter lacks it.
- The approved [constitution](./docs/constitution.md) sets six project principles for stack, specs, boundaries, tests, and English artifacts; apply it to future feature work.
- For game-record validation, DVD number zero normalizes to null; Notes are limited to 2,500 UTF-16 code units (`string.Length`), as approved for T03.
- Location validation uses medium-rooted `/` paths and no filesystem access; names such as `/mnt/usb/game.rar` are accepted by syntax without guessing a host mount point.
- Duplicate identity compares Medium and Location ordinally, plus normalized DVD Number only for DVD; the service must exclude the record being edited when checking others.
- The Core catalog contract uses synchronous operations; service results distinguish an absent record, field validation errors, and storage failures. Create returns the saved record; Edit validates before repository access, preserves the record ID, and excludes that ID from duplicate checks. Delete checks for the record and returns success only after repository deletion; the Desktop confirmation remains to be implemented. Desktop will reload the list after successful mutations.
- The [game record CRUD specification](./specs/001-crud-game/spec.md) defines required fields, DVD-only numbering, manually entered decompressed size, case-sensitive location/medium/DVD duplicate identity, confirmed record-only deletion, and offline editing. Its [implementation plan](./specs/001-crud-game/plan.md) guides [dependency-ordered tasks](./specs/001-crud-game/tasks.md); T01–T10 are complete, and implementation proceeds one approved task at a time.
