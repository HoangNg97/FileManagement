# Services

Back to [INDEX.md](INDEX.md). All services live in
[../src/LauncherCatalog/Services](../src/LauncherCatalog/Services) and contain no UI code.

## Repositories (SQLite persistence)

| Service | Responsibility |
|---------|----------------|
| `EntryRepository` | Catalog CRUD + settings; FTS5 + LIKE search (`SearchEntryIdsAsync`); legacy `entries.db`/`entries.json` migration. `StoragePath` → `catalog.db`. |
| `TaskRepository` | Task CRUD, status/priority, subtasks (`parent_task_id`), reminder bookkeeping; runtime column migration via `EnsureColumnAsync`. `StoragePath` → `task.db`. |
| `NoteRepository` | Note CRUD, favorites, last-viewed tracking. `StoragePath` → `note.db`. |
| `HistoryRepository` | History file + event CRUD; cascade delete; `DefaultStoragePath` → `history.db`. |
| `IssueRepository` | Issue + message CRUD for the Issues tab; parent `issues` (`id`, `title`, `note`, `created_at`, `updated_at`) / child `messages` tables with FK cascade delete; supports create/update/delete issue and add/update/delete message; migrates legacy `created_by`-only schemas by rebuilding the table; reads fail soft. `StoragePath` → `issue.db`. |

## Search & launching

| Service | Responsibility |
|---------|----------------|
| `EverythingSearchService` | Loads embedded `Everything64.dll`, starts bundled `Everything.exe` in the background, queries it directly (no `es.exe`). `IsAvailable`/`IsAppInstalled`, `EnsureStartedAsync`, `SearchAsync`. |
| `SearchService` | Catalog search orchestration/helpers. |
| `LauncherService` | Opens apps/folders, reveals in Explorer, copies paths. |
| `PathValidationService` | Validates/normalizes paths; detects missing targets. |
| `FilePickerService` (`IFilePickerService`) | File/folder picker dialogs. |
| `IconCacheService` | Extracts and caches file/app icons for list display. |
| `ChatContextExporter` | Serializes one main-tab DB (Catalog/Tasks/Note/History/Issues) to an indented JSON string used by the in-app chat panel as inline context inside the Gemini prompt. Returns `ContextData(Json, RowCount)`, or null when the tab is unsupported or empty. No files are produced. |

## UX / app infrastructure

| Service | Responsibility |
|---------|----------------|
| `LocalizationService` | Singleton (`Current`). Loads `Strings.resx`/`Strings.ja.resx`; `Get`/`Format`; `SetLanguage`; raises `CultureChanged`. |
| `LocExtension` | XAML markup extension `{loc:Loc Key}` bound to `LocalizationService`. |
| `ThemeService` | Light/dark theme application + persistence. |
| `DatabaseBackupService` | Timed backups of all four DBs into `Backups/Versions` (every 2 days, keep newest 10); writes `RESTORE.txt`. Uses `DatabaseBackupTarget`. |
| `TaskReminderService` | `DispatcherTimer` (5-min tick) evaluating reminder rules by status+priority; dedupes via `last_reminder_key`; raises tray notifications. Notifications are shown with long-lived balloon/toast behavior so they remain in Windows notification center until user dismissal. Morning ≥05:00, Afternoon 12:00–18:00. |
| `GlobalHotkeyService` + `HotkeyGesture` | Registers a system-wide hotkey; raises `HotkeyPressed` to open quick search. |
| `StartupLaunchService` | Adds/ensures the app in the current-user Windows `Run` key (`EnsureEnabled`). |
| `GeminiAuthService` | Google OAuth desktop flow via `GoogleWebAuthorizationBroker`; caches tokens locally; uses embedded `client_secret.json`. |
| `HistoryExcelExportService` | Exports history events to `.xlsx` (month/year view or all events). |
| `IssueExcelExportService` | Exports one or more issue threads to a styled `.xlsx` (raw OOXML, no external library) using a vertical Jira-style layout: per-issue two-column block (title banner, note/created-date/message-count metadata, "Conversation history" band, then each message as a shaded header row + wrapped body row). Multiple issues are placed side by side, separated by a thick vertical divider border; rows 1-5 are frozen; dates use a real date number format. Sheet name = sanitized title for a single issue, `Issues` for many. |
| `IssueConversationImportService` | Parses a conversation JSON file into `ImportedMessage` records for bulk import into an issue. Accepts `{ "messages": [ { "author", "date" (ISO-8601, optional), "content" } ] }` or a bare top-level array; `content` required, `date` optional (unparseable date → error). Caller merges results, skips exact duplicates, and re-sorts the thread newest-first. |
| `AiBridgeServer` | In-process ASP.NET Core / Kestrel HTTP server bridging WorkSync and the Gmail Chrome extension. Loopback-only on port `48717` (configurable); permissive CORS (`*`, `OPTIONS → 204`, `Max-Age 86400`); Bearer-token gate on the work endpoints. Endpoints: `GET /api/health` (open), `POST /api/analyze`, `POST /api/draft`. Requests run through `AiRequestQueue` (FIFO, one at a time, 60 s timeout → `504`) and delegate to an `IAiBackend`. The live backend is `GeminiWebDriver` (`providers: ["gemini"]`); `StubAiBackend` remains unused. `UpdateToken`, `GenerateToken`, `StartAsync`/`StopAsync`/`DisposeAsync`. Lifecycle owned by `AiBridgeManager`. See [AI-BRIDGE.md](AI-BRIDGE.md). |
| `AiBridgeManager` (`Services/Ai`) | Process-wide singleton (`Current`) that owns the single `AiBridgeServer` and its persisted settings (`ai_bridge_enabled`/`ai_bridge_port`/`ai_bridge_token`). Drives `InitializeAsync` (app load), `SetEnabledAsync`, `SetPortAsync` (restarts the listener), `RegenerateTokenAsync` (live token swap), `ShutdownAsync`. Detects port-in-use via a probe `TcpListener` bind and surfaces `AiBridgeStatus`. Re-exposes `GeminiWebDriver.SignInRequired` as `SignInRequired` so the shell can toast a persistent "Gemini sign-in required" notification on a silent sign-out. Also exposes `ChatAsync(prompt, ct)` / `ResetChatAsync()` used by the in-app chat panel (returns raw prose from a dedicated Gemini conversation slot; throws `AiBackendException` with `bridge_disabled`/`port_in_use`/`not_signed_in` when the panel should show an inline error). Shared by `MainWindow` and the AI Bridge tab. |
| `DpapiProtector` (`Services/Ai`) | DPAPI (`CurrentUser`) encrypt/decrypt of the bearer token at rest. Stores a `DPAPI:`-prefixed base64 blob; treats unprefixed values as legacy plaintext for transparent migration. |
| `AiRequestQueue` (`Services/Ai`) | `Channel`-based strict FIFO serializer: exactly one job runs at a time, later arrivals wait. Each job carries a wall-clock timeout (queue wait included) surfaced as `TimeoutException` → `504`. |
| `IAiBackend` / `StubAiBackend` (`Services/Ai`) | Abstraction over AI work (`AnalyzeAsync`/`DraftAsync`, `Providers`, `IsReady`) and the backend swap point. `StubAiBackend` (now unused) returns schema-valid canned responses; the live impl is `GeminiWebDriver`. |
| `GeminiWebDriver` (`Services/Ai`) | Live `IAiBackend`: drives the Gemini Web UI with Playwright (non-headless, persistent profile under `%LOCALAPPDATA%\LauncherCatalog\GeminiProfile`; channel preference msedge → chrome → bundled chromium). `LoginAsync` (interactive sign-in), per-request new-conversation → paste → wait-to-settle → scrape. `IsReady` reflects sign-in (→ 503 when out). Scrape/parse failures → 502. Serializes browser access with an internal lock. Hosts **two independent conversation slots on a single shared browser tab** — the **bridge** slot (used by `AnalyzeAsync`/`DraftAsync` for the Gmail extension; persisted state so reuse survives restarts) and the **chat** slot (used by `ChatAsync(prompt, ct)` for the in-app chat panel; in-memory state only). Context data reaches Gemini as **inline JSON** inside the prompt — no file upload path. A per-driver `_currentSlot` marker tracks which slot last used the tab; when a request comes in for the other slot the driver clicks Gemini's **New chat** (guarded by `ClickWithOverlayGuardAsync`, which dismisses CDK overlay backdrops before clicking) to keep their conversations separate. `ResetChatAsync()` forces the chat slot to start a fresh Gemini conversation on the next call. |
| `GeminiOutputSanitizer` (`Services/Ai`) | Static: strips `<think>` blocks + Markdown code fences and extracts the outermost balanced `{ … }` (string-aware) from scraped Gemini text; returns null when no JSON → 502. |
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
