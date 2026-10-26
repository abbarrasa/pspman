# Project memory

Keep this file short (about 50 lines maximum). Update it when implementation changes make a note obsolete; remove stale details instead of appending a history log. See [AGENTS.md](./AGENTS.md) for standing instructions and verification commands.

## Current state

- PSPMan aims to catalog, manage, and deploy PSP game collections across removable media. The [README](./README.md) gives the product overview.
- The repository is still an initial scaffold. No cataloging, deployment, removable-media handling, or persistence feature is implemented yet.
- [PSPMan.slnx](./PSPMan.slnx) contains Core, Infraestructure, Desktop, and Core.Tests projects targeting .NET 10.
- [Core](./src/PSPMan.Core/) and [Infraestructure](./src/PSPMan.Infraestructure/) each contain only a placeholder `Class1`; [Core.Tests](./tests/PSPMan.Core.Tests/) contains an empty sample test.
- [Desktop](./src/PSPMan.Desktop/) is the Avalonia starter app. `App.axaml.cs` creates `MainWindow` with `MainViewModel`; the window displays a sample greeting.
- SQLite and Microsoft.Data.Sqlite are the intended persistence stack, but the repository has no SQLite package reference, schema, database, or migrations.

## Details to remember

- `Infraestructure` is the existing project and directory spelling; keep it in paths and namespaces unless a rename is explicitly requested.
- [ViewLocator.cs](./src/PSPMan.Desktop/ViewLocator.cs) replaces `ViewModel` with `View` in the type's full name without changing the `ViewModels` namespace to `Views`. Check this mapping before relying on it for navigation.
- The repository-local [feature skill](./.agents/skills/feature/SKILL.md) requires a reviewable plan and user approval before changing files for a new feature.
- The skill-creator validator requires PyYAML; validate skills in an isolated Python environment when the system interpreter lacks it.
- The approved [constitution](./docs/constitution.md) sets six project principles for stack, specs, boundaries, tests, and English artifacts; apply it to future feature work.
- The [game record CRUD specification](./specs/001-crud-game/spec.md) defines required fields, DVD-only numbering, manually entered decompressed size, case-sensitive location/medium/DVD duplicate identity, confirmed record-only deletion, and offline editing; no open questions remain.
