# Architecture

Back to [INDEX.md](INDEX.md).

## Tech stack

- **Language/runtime:** C# on **.NET 8** (`net8.0-windows`).
- **UI:** WPF (`UseWPF`) with some WinForms interop (`UseWindowsForms`) for the tray icon and pickers.
- **Pattern:** MVVM (ViewModels expose `ICommand` + observable state; Views are XAML).
- **Persistence:** `Microsoft.Data.Sqlite` (one `.db` file per feature).
- **UI toolkit:** MaterialDesignThemes 5.3.1.
- **AI provider:** `GitHub.Copilot.SDK` (bundles the Copilot runtime under `runtimes\win-x64\native`, which must ship next to the EXE).
- **Local HTTP bridge:** ASP.NET Core / Kestrel (`Microsoft.AspNetCore.App` framework reference), self-hosted in-process for the Chrome extension.
- **Window chrome:** custom (`WindowStyle=None`) with an in-app title bar.

## Layered structure (`src/LauncherCatalog`)

| Layer | Folder | Responsibility |
|-------|--------|----------------|
| Models | `Models/` | Plain data types + enums. Some implement `INotifyPropertyChanged`. |
| Services | `Services/` | SQLite repositories, search, launching, theming, localization, backups, hotkeys, reminders, AI bridge. No UI. |
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
| AI Bridge | `AiBridgeViewModel` | `AiBridgeManager`, `AiBridgeServer` | settings in `catalog.db` |

`MainViewModel` drives three tabs (Catalog, Tasks, Note). AI Bridge has its own ViewModel.

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
  timestamped folder under `Backups/Versions` containing all three DBs; keeps newest 10; writes
  `Backups/RESTORE.txt`.
- **Startup registration** via `StartupLaunchService`: adds the app to the current-user Windows
  `Run` key so it launches at sign-in.
- **Theming** via `ThemeService`: light/dark, toggled from the title bar, persisted in settings.
- **Localization** via `LocalizationService`: runtime culture switch; raises `CultureChanged` so
  ViewModels refresh localized strings without restart.
- **Local AI Bridge** via `AiBridgeServer` (ASP.NET Core / Kestrel), lifecycle owned by
  `AiBridgeManager` (singleton): starts/stops with the app and with the enable toggle. Bound to
  **loopback only** on port **48717** (configurable via `settings.ai_bridge_port`). CORS is
  permissive (`*`, `OPTIONS → 204`) for `chrome-extension://` callers, and a **Bearer token**
  (`settings.ai_bridge_token`, DPAPI-encrypted) is required on `POST /api/analyze` and
  `POST /api/draft`; `GET /api/health` is open for discovery. Requests run through a strict **FIFO
  queue** (one at a time, 60 s budget each) and delegate to a swappable `IAiBackend`. A dedicated
  **"AI Bridge" tab** manages GitHub Copilot sign-in (top card) plus enable/port/token. The backend is
  `CopilotBackend` (GitHub Copilot SDK, signed-in user's subscription); work endpoints return **503**
  until a user signs in. See [AI-BRIDGE.md](AI-BRIDGE.md) for the full design.

## Storage locations

All under `%LOCALAPPDATA%\LauncherCatalog` (legacy folder name kept for backward compatibility):

```text
catalog.db   task.db   note.db
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
| Publish (self-contained folder) | `.\scripts\publish-windows.ps1` (cleans, `dotnet publish ... -c Release -r win-x64 --self-contained true -o .\dist\WorkSync-win-x64\app`, then compiles the root launcher `WorkSync.exe` from `scripts\WorkSyncLauncher.cs`) |

VS Code tasks `build`, `run`, and `publish win-x64` mirror the commands above.

## Bundled / embedded assets

- `Everything.exe` + `Everything.lng` copied to output (`Everything/Runtime`).
- `Everything64.dll` **embedded** as `LauncherCatalog.Embedded.Everything64.dll`.
