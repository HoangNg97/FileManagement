# Conventions

Back to [INDEX.md](INDEX.md).

## Documentation sync rule

> [!IMPORTANT]
> **Any change that creates a difference between the project and the docs must update the docs
> immediately, in the same change set.** This applies to features, behavior, database schema,
> services, ViewModels/Views, commands, build steps, and architecture.
>
> When you change code, update **all** of the following that are affected, together with the code:
> - [../README.md](../README.md) — user-facing overview/behavior.
> - [INDEX.md](INDEX.md) — if the doc map itself changes.
> - [ARCHITECTURE.md](ARCHITECTURE.md), [DATA-MODEL.md](DATA-MODEL.md), [SERVICES.md](SERVICES.md),
>   [VIEWMODELS-AND-VIEWS.md](VIEWMODELS-AND-VIEWS.md) — for the area you touched.
>
> Treat stale documentation as a defect equal to a failing test.

## Localization

- All user-facing text is localized. **Do not hardcode** strings in XAML or C#.
- Add new strings to both resource files:
  - [../src/LauncherCatalog/Resources/Strings.resx](../src/LauncherCatalog/Resources/Strings.resx) (English)
  - [../src/LauncherCatalog/Resources/Strings.ja.resx](../src/LauncherCatalog/Resources/Strings.ja.resx) (Japanese)
- In XAML use the markup extension: `Text="{loc:Loc Some.Resource.Key}"`.
- In C# use `LocalizationService.Current.Get("Key")` / `.Format("Key", args)`.
- Language switches at runtime via `CultureChanged`; ensure new VM strings refresh on that event.

## Naming / structure

- Namespace stays `LauncherCatalog` even though the product/assembly is `WorkSync` (legacy, kept on purpose).
- Keep the layering: Views → ViewModels → Services → Models. Services must not reference WPF UI types.
- New persistence goes through a repository in `Services/`; follow existing patterns
  (ISO-8601 timestamps, GUID text keys, `tags_json` arrays, `EnsureColumnAsync` for additive migrations).
- Prefer additive, backward-compatible schema migrations (the app auto-adds new columns/files).

## Data & paths

- All app data is under `%LOCALAPPDATA%\LauncherCatalog` (legacy folder name kept).
- Don't break the legacy migrations (`entries.db`→`catalog.db`, `entries.json` import).

## Logging & crashes

- Use `AppLogger.WriteInfo` / `AppLogger.WriteCrash`. Logs go to `Logs/` with `latest.log` mirror.

## Build/publish

- Use the VS Code tasks (`build`, `run`, `publish win-x64`) or the equivalent commands in
  [ARCHITECTURE.md](ARCHITECTURE.md#build--run). Single-EXE publish output: `dist/WorkSync-win-x64`.
