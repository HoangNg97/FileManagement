# WorkSync Documentation Map

This is the entry point for understanding the project **without reading the entire codebase**.

> **How to use this in a new chat:** Tell the assistant *"Read README.md"*. The README points
> here, and this file links to focused docs. Read the docs relevant to your task, then dive into
> source files only where deeper detail is required.

## Read order

1. [../README.md](../README.md) — product overview + user-facing feature behavior.
2. [ARCHITECTURE.md](ARCHITECTURE.md) — tech stack, layering, app lifecycle, app-wide behaviors.
3. [DATA-MODEL.md](DATA-MODEL.md) — SQLite schemas + model classes per feature.
4. [SERVICES.md](SERVICES.md) — what every service in `Services/` does.
5. [VIEWMODELS-AND-VIEWS.md](VIEWMODELS-AND-VIEWS.md) — MVVM map: ViewModels, Views, Converters.
6. [CONVENTIONS.md](CONVENTIONS.md) — coding/localization conventions and the doc-sync rule.

Partially built features:
- [AI-BRIDGE.md](AI-BRIDGE.md) — local AI gateway for the Gmail Chrome extension (Gemini via browser automation). **All three stages built: HTTP contract + settings tab + live `GeminiWebDriver` (Playwright). Selectors target the live Gemini DOM and may need tuning.**

## One-paragraph summary

WorkSync is a Windows desktop app (C# / WPF / .NET 8, MVVM, custom window chrome) with four
feature tabs — **Catalog**, **Tasks**, **Note**, **History Management** — each backed by its own
SQLite database under `%LOCALAPPDATA%\LauncherCatalog`. It runs single-instance, lives in the tray,
supports a global quick-search hotkey, runtime language switching (English/Japanese), light/dark
theming, automatic backups, optional [Everything](https://www.voidtools.com/) file search, and
Gemini OAuth.

## Solution / project

- Solution: [../FileLauncherCatalog.sln](../FileLauncherCatalog.sln)
- Main project: [../src/LauncherCatalog/LauncherCatalog.csproj](../src/LauncherCatalog/LauncherCatalog.csproj)
- Assembly/Product name: `WorkSync`. Root namespace: `LauncherCatalog` (legacy, intentionally kept).

> [!IMPORTANT]
> **Documentation sync rule:** Any change that creates a difference between the project and these
> docs (features, behavior, schema, services, ViewModels/Views, commands, architecture) must update
> the relevant `docs/*.md` file **and** `README.md` **in the same change set**, immediately. Stale
> docs are treated as a defect. See [CONVENTIONS.md](CONVENTIONS.md#documentation-sync-rule).
