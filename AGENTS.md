# Agent instructions

## Project

These instructions apply to the entire repository. Act as an expert C# and .NET developer. Respond to the user and write functional documentation in English; use technical names for classes, methods, and variables that are consistent with the existing code.

PSPMan is a cross-platform desktop application for cataloging, managing, and deploying PSP game collections stored on removable media (DVDs, USB drives, and Memory Sticks). The `PSPMan.slnx` solution uses C# and .NET 10. The UI uses Avalonia 12 and CommunityToolkit.Mvvm; tests use xUnit.

## Technologies and structure

Technologies:
- The `PSPMan.slnx` solution uses C# and .NET 10.
- The UI uses Avalonia 12 and CommunityToolkit.Mvvm.
- Persistence uses SQLite and Microsoft.Data.Sqlite.
- Tests use xUnit.

Structure:
- `src/PSPMan.Core/`: domain and logic independent of the UI and persistence. It must not depend on Desktop or Infraestructure.
- `src/PSPMan.Infraestructure/`: storage and data-access adapters; depends on Core. Keep the existing `Infraestructure` spelling in the project name.
- `src/PSPMan.Desktop/`: Avalonia application, views (`Views/`), view models (`ViewModels/`), and application composition; depends on Core and Infraestructure.
- `tests/PSPMan.Core.Tests/`: Core tests using xUnit.

Do not add frameworks, services, or dependencies for features that are not yet needed.

## Memory

Use [MEMORY.md](./MEMORY.md) for concise project context that remains useful across sessions.

- Read it before starting work.
- At the end of each task, review it and update it when the implemented state, an important decision and its reason, or a repeatable pitfall has changed.
- Keep it to about 50 lines. Replace outdated notes instead of appending a task log, and avoid repeating instructions from this file.
- If a note becomes a permanent rule, propose moving it to `AGENTS.md`.
- Never record secrets, tokens, personal data, or transient local or container state.
- Always: update `MEMORY.md` upon completing each task.

## Implementing changes

- Keep business logic out of views and UI code; use view models for UI state and actions.
- Preserve Avalonia's typed bindings when adding views. If you use `ViewLocator` for navigation, verify that it resolves each view's namespace correctly.
- Design removable-media access for different paths and operating systems; check that a medium is available before reading or copying, and avoid modifying user files without an explicit action.
- SQLite is part of the planned persistence technology. Add persistence in Infraestructure when a task requires it, and do not assume an existing database.
- Add useful tests incrementally for the requested functionality; avoid expanding the scope unnecessarily.

## Working approach

- Do only what is requested; do not add features on your own.
- Make small, focused changes; do not rewrite code that already works.
- When finished, summarize what changed and any decisions the user should review.

## Verification

- Build from the repository root with `dotnet build PSPMan.slnx`.
- Run tests with `dotnet test PSPMan.slnx`.
- For UI changes, also check startup with `dotnet run --project src/PSPMan.Desktop/PSPMan.Desktop.csproj` when a graphical environment is available.
