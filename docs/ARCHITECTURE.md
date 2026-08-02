# Architecture

Back to [INDEX.md](INDEX.md).

## Tech stack

- **Language/runtime:** C# on **.NET 8** (`net8.0-windows`).
- **UI:** WPF (`UseWPF`) with some WinForms interop (`UseWindowsForms`) for the tray icon and pickers.
- **Pattern:** MVVM (ViewModels expose `ICommand` + observable state; Views are XAML).
- **Persistence:** `Microsoft.Data.Sqlite` (one `.db` file per feature).
- **UI toolkit:** MaterialDesignThemes 5.3.1.
- **Auth:** `Google.Apis.Auth` (Gemini OAuth).
- **Local HTTP bridge:** ASP.NET Core / Kestrel (`Microsoft.AspNetCore.App` framework reference), self-hosted in-process for the Chrome extension.
- **Window chrome:** custom (`WindowStyle=None`) with an in-app title bar.

## Layered structure (`src/LauncherCatalog`)

| Layer | Folder | Responsibility |
|-------|--------|----------------|
| Models | `Models/` | Plain data types + enums. Some implement `INotifyPropertyChanged`. |
| Services | `Services/` | SQLite repositories, search, launching, theming, localization, backups, hotkeys, reminders, auth. No UI. |
| ViewModels | `ViewModels/` | MVVM state + commands. Bridge between Services and Views. |
| Views | `Views/` | Dialog windows + feature views (XAML + code-behind). |
| Converters | `Converters/` | XAML `IValueConverter` presentation helpers. |
| Resources | `Resources/` | `Strings.resx` (en) + `Strings.ja.resx` (ja) localization tables. |

Composition root is [../src/LauncherCatalog/MainWindow.xaml.cs](../src/LauncherCatalog/MainWindow.xaml.cs):
it constructs the repositories/services and injects them into `MainViewModel`. There is **no DI
container**; dependencies are wired manually in `MainWindow`.

## Tabs → ViewModel → DB

| Tab | ViewModel | Repository / Service | DB file |
|-----|-----------|----------------------|---------|
| Catalog | `MainViewModel` | `EntryRepository`, `EverythingSearchService`, `LauncherService` | `catalog.db` |
| Tasks | `MainViewModel` | `TaskRepository`, `TaskReminderService` | `task.db` |
| Note | `MainViewModel` | `NoteRepository` | `note.db` |
| History Management | `HistoryManagementViewModel` | `HistoryRepository`, `HistoryExcelExportService` | `history.db` |

`MainViewModel` drives three tabs (Catalog, Tasks, Note). History has its own ViewModel.

### In-app chat overlay

A title-bar chat button opens `Views/ChatPanel` (`ChatViewModel`) as a bubble-style overlay above
the tab content. The panel reads which tab is currently selected in `MainTabs`, serializes that
tab's DB rows through `ChatContextExporter` as **inline JSON**, and sends the question through
`AiBridgeManager.ChatAsync` — the same live driver used by the Gmail bridge. When the JSON is
short it rides along with the question in a single Gemini turn; when it's too large the panel
splits it at line boundaries and sends the parts across multiple sequential turns in the same
Gemini conversation (intermediate turns acknowledge with "OK", the final turn carries the actual
question). The chat and the Gmail bridge share **one** Gemini browser tab; the driver tracks
which slot last drove the tab and clicks Gemini's **New chat** on every slot switch so their
conversations stay separate. Chat history in the panel is in-memory only.

## Application lifecycle

Defined in [../src/LauncherCatalog/App.xaml.cs](../src/LauncherCatalog/App.xaml.cs):

1. **Crash handlers** registered first (`DispatcherUnhandledException`, `AppDomain.UnhandledException`,
   `TaskScheduler.UnobservedTaskException`) → write to `Logs/` (mirrored to `latest.log`).
2. **Single instance** enforced via a named `Mutex` (`Local\LauncherCatalog.SingleInstance`).
   A second launch signals an `EventWaitHandle` (`Local\LauncherCatalog.Activate`) so the running
   instance activates its window, then the second process exits.
3. `MainWindow.Loaded` initializes the task DB, loads the ViewModel, starts background services
   (backups, startup registration, Everything, task reminders) and registers the quick-search hotkey.

## App-wide behaviors

- **Tray icon** (WinForms `NotifyIcon`) with Open/Quit; closing minimizes to tray, real quit via menu.
- **Global quick-search hotkey** via `GlobalHotkeyService` opens `QuickSearchWindow` even when the
  main window is visible.
- **Automatic backups** via `DatabaseBackupService`: when the last backup is >2 days old, writes one
  timestamped folder under `Backups/Versions` containing all four DBs; keeps newest 10; writes
  `Backups/RESTORE.txt`.
- **Startup registration** via `StartupLaunchService`: adds the app to the current-user Windows
  `Run` key so it launches at sign-in.
- **Theming** via `ThemeService`: light/dark, toggled from the title bar, persisted in settings.
- **Localization** via `LocalizationService`: runtime culture switch; raises `CultureChanged` so
  ViewModels refresh localized strings without restart.
- **In-app chat (Ask Gemini)**: a title-bar chat button opens the `ChatPanel` overlay, which sends
  the currently-focused tab's data (Catalog/Tasks/Note/History/Issues) as **inline JSON** in the
  Gemini prompt through `AiBridgeManager.ChatAsync`, chunking across multiple sequential turns
  when the JSON is too large for a single turn. Reuses the same Gemini browser tab as the Gmail
  bridge; the driver clicks Gemini's **New chat** whenever the active slot switches (bridge ↔ chat)
  so their conversations stay separate. Errors (bridge disabled, port in use, not signed in)
  surface as an inline banner above the input; the chat history itself is in-memory only.
- **Local AI Bridge** via `AiBridgeServer` (ASP.NET Core / Kestrel), lifecycle owned by
  `AiBridgeManager` (singleton): starts/stops with the app and with the enable toggle. Bound to
  **loopback only** on port **48717** (configurable via `settings.ai_bridge_port`). CORS is
  permissive (`*`, `OPTIONS → 204`) for `chrome-extension://` callers, and a **Bearer token**
  (`settings.ai_bridge_token`, DPAPI-encrypted) is required on `POST /api/analyze` and
  `POST /api/draft`; `GET /api/health` is open for discovery. Requests run through a strict **FIFO
  queue** (one at a time, 60 s budget each) and delegate to a swappable `IAiBackend`. A dedicated
  **"AI Bridge" tab** manages enable/port/token and Gemini sign-in. The live backend is
  **`GeminiWebDriver`** — Playwright automation of the Gemini Web UI (non-headless, persistent
  profile, installed Edge/Chrome channel preferred), returning **503** until the user signs in.** See
  [AI-BRIDGE.md](AI-BRIDGE.md) for the full design and stage status.

## Storage locations

All under `%LOCALAPPDATA%\LauncherCatalog` (legacy folder name kept for backward compatibility):

```text
catalog.db   task.db   note.db   history.db
settings (table inside catalog.db)
Backups/Versions/...    Backups/RESTORE.txt
Logs/  (Logs/latest.log)
```

See [DATA-MODEL.md](DATA-MODEL.md) for schemas and migrations.

## Build / run

| Action | Command |
|--------|---------|
| Build | `dotnet build .\FileLauncherCatalog.sln -c Debug` |
| Run | `dotnet run --project .\src\LauncherCatalog\LauncherCatalog.csproj` |
| Publish (single EXE) | `dotnet publish .\src\LauncherCatalog\LauncherCatalog.csproj -c Release -r win-x64 --self-contained true -o .\dist\WorkSync-win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` |

VS Code tasks `build`, `run`, and `publish win-x64` mirror the commands above.

## Bundled / embedded assets

- `Everything.exe` + `Everything.lng` copied to output (`Everything/Runtime`).
- `Everything64.dll` **embedded** as `LauncherCatalog.Embedded.Everything64.dll`.
- `client_secret.json` **embedded** into the assembly (single-EXE friendly Gemini auth).
