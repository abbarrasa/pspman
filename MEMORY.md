# Project memory

Keep this file short (about 50 lines maximum). Update it when implementation changes make a note obsolete; remove stale details instead of appending a history log. See [AGENTS.md](./AGENTS.md) for standing instructions and verification commands.

## Current state

- PSPMan aims to catalog, manage, and deploy PSP game collections across removable media. The [README](./README.md) gives the product overview.
- The repository is still an initial scaffold. Core has game record/input types, pure rules, and catalog list/read/create/edit use cases; delete, deployment, removable-media handling, and persistence are not implemented yet.
- [PSPMan.slnx](./PSPMan.slnx) contains Core, Infraestructure, Desktop, and Core.Tests projects targeting .NET 10.
- [Core](./src/PSPMan.Core/) contains game record types, rules, and a repository contract/service with create and edit support alongside a placeholder `Class1`; [Infraestructure](./src/PSPMan.Infraestructure/) contains only a placeholder `Class1`; [Core.Tests](./tests/PSPMan.Core.Tests/) contains rule/service tests and an empty sample test.
- [Desktop](./src/PSPMan.Desktop/) is the Avalonia starter app. `App.axaml.cs` creates `MainWindow` with `MainViewModel`; the window displays a sample greeting.
- SQLite and Microsoft.Data.Sqlite are the intended persistence stack, but the repository has no SQLite package reference, schema, database, or migrations.

## Details to remember

- `Infraestructure` is the existing project and directory spelling; keep it in paths and namespaces unless a rename is explicitly requested.
- [ViewLocator.cs](./src/PSPMan.Desktop/ViewLocator.cs) replaces `ViewModel` with `View` in the type's full name without changing the `ViewModels` namespace to `Views`. Check this mapping before relying on it for navigation.
- The repository-local [feature skill](./.agents/skills/feature/SKILL.md) requires a reviewable plan and user approval before changing files for a new feature.
- The skill-creator validator requires PyYAML; validate skills in an isolated Python environment when the system interpreter lacks it.
- The approved [constitution](./docs/constitution.md) sets six project principles for stack, specs, boundaries, tests, and English artifacts; apply it to future feature work.
- For game-record validation, DVD number zero normalizes to null; Notes are limited to 2,500 UTF-16 code units (`string.Length`), as approved for T03.
- Location validation uses medium-rooted `/` paths and no filesystem access; names such as `/mnt/usb/game.rar` are accepted by syntax without guessing a host mount point.
- Duplicate identity compares Medium and Location ordinally, plus normalized DVD Number only for DVD; the service must exclude the record being edited when checking others.
- The Core catalog contract uses synchronous operations; service results distinguish an absent record, field validation errors, and storage failures. Create returns the saved record; Edit validates before repository access, preserves the record ID, excludes that ID from duplicate checks, and reports failed writes without presenting them as saved. Desktop will reload the list after success.
- The [game record CRUD specification](./specs/001-crud-game/spec.md) defines required fields, DVD-only numbering, manually entered decompressed size, case-sensitive location/medium/DVD duplicate identity, confirmed record-only deletion, and offline editing. Its [implementation plan](./specs/001-crud-game/plan.md) guides [dependency-ordered tasks](./specs/001-crud-game/tasks.md); T01–T08 are complete, and implementation proceeds one approved task at a time.
