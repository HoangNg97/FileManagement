# Services

Back to [INDEX.md](INDEX.md). All services live in
[../src/LauncherCatalog/Services](../src/LauncherCatalog/Services) and contain no UI code.

## Repositories (SQLite persistence)

| Service | Responsibility |
|---------|----------------|
| `EntryRepository` | Catalog CRUD + settings; FTS5 + LIKE search (`SearchEntryIdsAsync`); legacy `entries.db`/`entries.json` migration. `StoragePath` → `catalog.db`. |
| `TaskRepository` | Task CRUD, status/priority, subtasks (`parent_task_id`), reminder bookkeeping; runtime column migration via `EnsureColumnAsync`. `StoragePath` → `task.db`. |
| `NoteRepository` | Note CRUD, favorites, last-viewed tracking. `StoragePath` → `note.db`. |


## Search & launching

| Service | Responsibility |
|---------|----------------|
| `EverythingSearchService` | Loads embedded `Everything64.dll`, starts bundled `Everything.exe` in the background, queries it directly (no `es.exe`). `IsAvailable`/`IsAppInstalled`, `EnsureStartedAsync`, `SearchAsync`. |
| `LauncherService` | Opens apps/folders, reveals in Explorer, copies paths. |
| `PathValidationService` | Validates/normalizes paths; detects missing targets. |
| `FilePickerService` (`IFilePickerService`) | File/folder picker dialogs. |
| `IconCacheService` | Extracts and caches file/app icons for list display. |

## UX / app infrastructure

| Service | Responsibility |
|---------|----------------|
| `LocalizationService` | Singleton (`Current`). Loads `Strings.resx`/`Strings.ja.resx`; `Get`/`Format`; `SetLanguage`; raises `CultureChanged`. |
| `LocExtension` | XAML markup extension `{loc:Loc Key}` bound to `LocalizationService`. |
| `ThemeService` | Light/dark theme application + persistence. |
| `DatabaseBackupService` | Timed backups of the catalog/task/note DBs into `Backups/Versions` (every 2 days, keep newest 10); writes `RESTORE.txt`. Uses `DatabaseBackupTarget`. |
| `TaskReminderService` | `DispatcherTimer` (5-min tick) evaluating reminder rules by status+priority; dedupes via `last_reminder_key`; raises tray notifications. Notifications are shown with long-lived balloon/toast behavior so they remain in Windows notification center until user dismissal. Morning ≥05:00, Afternoon 12:00–18:00. |
| `GlobalHotkeyService` + `HotkeyGesture` | Registers a system-wide hotkey; raises `HotkeyPressed` to open quick search. |
| `StartupLaunchService` | Adds/ensures the app in the current-user Windows `Run` key (`EnsureEnabled`). |
| `AiBridgeServer` | In-process ASP.NET Core / Kestrel HTTP server bridging WorkSync and the Gmail Chrome extension. Loopback-only on port `48717` (configurable); permissive CORS (`*`, `OPTIONS -> 204`, `Max-Age 86400`); Bearer-token gate on the work endpoints. Endpoints: `GET /api/health` (open), `POST /api/analyze`, `POST /api/draft`. Requests run through `AiRequestQueue` (FIFO, one at a time, 60 s timeout -> `504`) and delegate to an `IAiBackend` (503 while the backend is not ready). `UpdateToken`, `GenerateToken`, `StartAsync`/`StopAsync`/`DisposeAsync`. Lifecycle owned by `AiBridgeManager`. See [AI-BRIDGE.md](AI-BRIDGE.md). |
| `AiBridgeManager` (`Services/Ai`) | Process-wide singleton (`Current`) that owns the single `AiBridgeServer`, its `CopilotBackend`, and its persisted settings (`ai_bridge_enabled`/`ai_bridge_port`/`ai_bridge_token`). Exposes `IsSignedIn`/`SignedInLogin` and `SignInAsync`/`SignOutAsync` (delegated to the backend). Drives `InitializeAsync` (app load), `SetEnabledAsync`, `SetPortAsync` (restarts the listener), `RegenerateTokenAsync` (live token swap), `ShutdownAsync`. Detects port-in-use via a probe `TcpListener` bind and surfaces `AiBridgeStatus`. Shared by `MainWindow` and the AI Bridge tab. |
| `DpapiProtector` (`Services/Ai`) | DPAPI (`CurrentUser`) encrypt/decrypt of the bearer token at rest. Stores a `DPAPI:`-prefixed base64 blob; treats unprefixed values as legacy plaintext for transparent migration. |
| `AiRequestQueue` (`Services/Ai`) | `Channel`-based strict FIFO serializer: exactly one job runs at a time, later arrivals wait. Each job carries a wall-clock timeout (queue wait included) surfaced as `TimeoutException` → `504`. |
| `IAiBackend` (`Services/Ai`) | Abstraction over AI work (`AnalyzeAsync`/`DraftAsync`, `Providers`, `IsReady`) and the backend swap point. |
| `CopilotBackend` (`Services/Ai`) | Live `IAiBackend` on the GitHub Copilot SDK (`providers: ["github-copilot"]`). `SignInAsync` runs GitHub's OAuth device flow (WorkSync's built-in OAuth App client ID `ClientId`; scope `read:user`; unknown ID / disabled device flow -> readable error), opens only an `https://github.com` verification URL, verifies the account via `GetAuthStatusAsync`, and stores the token DPAPI-encrypted (`ai_copilot_token`/`ai_copilot_login`). `IsReady` = signed in (503 otherwise). Each request: `CopilotClientMode.Empty` client (home `%LOCALAPPDATA%\LauncherCatalog\Copilot`), fresh session with **no tools**, replaced system message, `SendAndWaitAsync<T>` structured output into `AiAnalyzeResult`/`AiDraftResult`; the session is deleted afterwards. Failures -> `AiBackendException` (502); timeouts pass through (504). |
| `AppLogger` | Static logger: `WriteInfo`, `WriteCrash`; writes to `Logs/` and mirrors `latest.log`. |

## Dialog services

| Service | Responsibility |
|---------|----------------|
| `EntryDialogService` (`IEntryDialogService`) | Add/edit catalog entry dialogs. |
| `TaskDialogService` (`ITaskDialogService`) | Create/edit task dialogs. |

## Reminder rules (implemented in `TaskReminderService`)

- `Done`: none.
- `On-going` + `Urgent`: morning + afternoon.
- `On-going` + `Normal`: morning only.
- `On-going` + `Low`: none.
- `Todo` + `Urgent`: every 3 hours.
- `Todo` + `Normal`: morning + afternoon.
- `Todo` + `Low`: morning only.

> Service responsibility changes **must** update this file and `README.md` in the same change set.
