# PSPMan Constitution

1. **Minimal stack.** Use the existing C#/.NET 10, Avalonia, CommunityToolkit.Mvvm, SQLite/Microsoft.Data.Sqlite, and xUnit stack; justify every new dependency with a current need.
2. **Spec and code agree.** Define each feature's observable behavior in an approved spec before coding; update the spec, code, and tests together when behavior changes.
3. **Independent core.** Keep domain models and business rules in Core, without UI, persistence, or platform dependencies.
4. **Clear boundaries.** Desktop owns UI and view models; Infraestructure owns storage and device adapters; both use Core contracts, and neither puts implementation details in Core.
5. **Meaningful coverage.** Test rules with unit tests, storage and device behavior with integration tests, and affected user flows with functional tests; measure coverage and justify significant gaps.
6. **English artifacts.** Write identifiers, comments, specs, documentation, and user-facing UI text in English.
