# WorkSync

WorkSync is a Windows desktop app built with C#, WPF, and .NET 8.

It currently has 4 main tabs:
- `Catalog`: searchable launcher list for apps/folders.
- `Tasks`: task tracker with SQLite-backed create/edit/complete flow.
- `Note`: local note workspace with search, favorites, tags, live editing, and preview.
- `AI Bridge`: settings for the local AI gateway used by the Gmail Chrome extension.

## Documentation Map (read this first)

This README is the user-facing overview. For a complete technical understanding **without reading
the whole codebase**, follow the documentation map:

- **Start here:** [docs/INDEX.md](docs/INDEX.md) �? links to every doc in read order.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) �? tech stack, layering, lifecycle, app-wide behaviors.
- [docs/DATA-MODEL.md](docs/DATA-MODEL.md) �? SQLite schemas + models per feature.
- [docs/SERVICES.md](docs/SERVICES.md) �? what every service does.
- [docs/VIEWMODELS-AND-VIEWS.md](docs/VIEWMODELS-AND-VIEWS.md) �? MVVM map (ViewModels, Views, Converters).
- [docs/CONVENTIONS.md](docs/CONVENTIONS.md) �? coding/localization conventions + the doc-sync rule.
- [docs/AI-BRIDGE.md](docs/AI-BRIDGE.md) �? local AI gateway (HTTP contract, GitHub Copilot sign-in, settings).

> [!IMPORTANT]
> **Mandatory reading rule (for AI assistants and new contributors):** reading this README alone is
> **not enough**. Whenever you are asked to *"read README.md"* (or start a new chat on this project),
> you **must also read every file in the documentation map above** before doing any work �? that is:
> [docs/INDEX.md](docs/INDEX.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
> [docs/DATA-MODEL.md](docs/DATA-MODEL.md), [docs/SERVICES.md](docs/SERVICES.md),
> [docs/VIEWMODELS-AND-VIEWS.md](docs/VIEWMODELS-AND-VIEWS.md), [docs/CONVENTIONS.md](docs/CONVENTIONS.md),
> and [docs/AI-BRIDGE.md](docs/AI-BRIDGE.md). Skip a file only if you have already read it in the
> current session. Do not answer questions or make code changes until all of them have been read.

> **Documentation sync rule:** any change that makes the project differ from these docs (features,
> behavior, schema, services, ViewModels/Views, commands, architecture) **must** update the affected
> `docs/*.md` file(s) and this README **in the same change set, immediately**. Stale docs are a defect.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK with Windows Desktop support
- Visual Studio Code with the C# extension

The project uses `Microsoft.Data.Sqlite` for local storage.

Everything integration is optional. This project can bundle `Everything.exe` together with the `Everything64.dll` SDK so the app can start Everything in the background and query it directly without relying on a separate `es.exe` install. The source tree keeps only the required runtime files under `Everything/Runtime`.

## Run

From VS Code tasks:
- `build`
- `run`
- `publish win-x64`

Equivalent terminal commands:

```powershell
dotnet build .\FileLauncherCatalog.sln
dotnet run --project .\src\LauncherCatalog\LauncherCatalog.csproj
```

## Publish (ready-to-run folder)

```powershell
.\scripts\publish-windows.ps1
```

This runs a self-contained folder publish (no .NET install needed on the target PC). It deletes the old
output first, then produces:

```text
dist\WorkSync-win-x64\
  WorkSync.exe   small launcher (starts app\WorkSync.exe)
  app\           the real application and all of its files
```

Copy the **whole folder** and run the root `WorkSync.exe`. The launcher is built from
`scripts\WorkSyncLauncher.cs` with the .NET Framework `csc.exe` that ships with Windows, so it needs
nothing extra. Keep `app\` intact: the Copilot runtime (`app\runtimes\win-x64\native`) and
`Everything.exe` must stay next to `app\WorkSync.exe`.

## Current UI/UX Notes

- Custom window chrome is enabled (`WindowStyle=None`) with an in-app title bar.
- The title bar includes:
  - language selector with runtime switching
  - theme toggle (dark/light)
  - a gear button that opens the **Settings** window
  - minimize / maximize-restore / close buttons
- The Settings window uses a left category list (currently a single **General** tab, designed to grow) and a right detail panel. General exposes theme (dark/light), language, and the catalog quick-search shortcut. All changes apply immediately as they are made, so the **OK** button only closes the dialog.
- Title bar supports drag from the full empty caption area and double-click maximize/restore.
- Task due-date selection uses a text-first field that opens a Material Design popup calendar and follows both light and dark mode.
- Catalog/task filter dropdowns use padded left-aligned labels, and task due-date/action controls use compact sizing to preserve table readability.
- UI text is centralized for localization and can switch languages without restarting the app.

## Localization

- Current built-in languages:
  - `English`
  - `Japanese`
- Language changes are applied at runtime from the title bar language dropdown.
- The app registers itself to launch at Windows sign-in for the current user.
- User-facing text is centralized in:

```text
src/LauncherCatalog/Resources/Strings.resx
src/LauncherCatalog/Resources/Strings.ja.resx
```

- Runtime localization infrastructure:
  - `Services/LocalizationService.cs`
  - `Services/LocExtension.cs`
- XAML text should use the localization markup extension instead of hardcoded strings:

```xaml
Text="{loc:Loc Some.Resource.Key}"
```

- New user-facing strings should be added to the resource files instead of being hardcoded in XAML or C#.

## Catalog Feature

- Add app/file entries from file path
- Add folder entries from folder picker
- Edit display name, path, tags, note
- Search by display name, file/folder names, full path, tags, note
- Optional Everything results shown below catalog results
- Open, reveal in Explorer, copy path, delete (without deleting files)
- Missing-path detection with status badge
- Favorites and sorting/filtering support

## Tasks Feature

Tasks are persisted in `task.db` and shown in the `Tasks` tab list.

Current task capabilities:
- Create task (dialog)
- Edit task (icon button in each row)
- Remove task from the list/database from the `Mark as Completed` action
- Toggle task reminders from a bell button in each task row

Task dialog behavior:
- Create mode:
  - `Status` fixed to `Todo` (read-only)
  - `Priority`: `Low`, `Normal`, `Urgent` (default `Normal`)
  - `Due Date`: displayed as date text in a simple field; clicking it opens a Material Design popup calendar
  - task reminders default to enabled
- Edit mode:
  - all fields editable
  - action button label is `Edit`

Task reminder rules:
- `Done`: no reminders
- `On-going` + `Urgent`: morning and afternoon reminders
- `On-going` + `Normal`: morning reminder only
- `On-going` + `Low`: no reminders
- `Todo` + `Urgent`: reminder every 3 hours
- `Todo` + `Normal`: morning and afternoon reminders
- `Todo` + `Low`: morning reminder only

Reminder notification behavior:
- Task reminders are shown as Windows tray notifications and kept in the notification center until you dismiss them.
- Clicking a task reminder restores the app window.

## Note Feature

Notes are persisted in `note.db` and shown in the `Note` tab workspace.

Current note capabilities:
- Create and delete notes
- Edit note title, tags, and body in-place
- Debounced auto-save while typing
- Search notes by title, body, or tags
- Filter notes by `All Notes`, `Favorites`, `Recent`, or `Tagged`
- Favorite notes for quick access
- Track last viewed time for recent-note sorting
- Detect `http`/`https` URLs directly inside the editable note body after a brief typing pause and open them in the default browser with a single click
- Split note workspace with list and editor panels

## Storage

For backward compatibility, local app data still uses the legacy `LauncherCatalog` folder name.

All app data is under:

```text
%LOCALAPPDATA%\LauncherCatalog
```

### Catalog DB

```text
%LOCALAPPDATA%\LauncherCatalog\catalog.db
```

Notes:
- Legacy SQLite filename `entries.db` is auto-migrated to `catalog.db` on startup if needed.
- Legacy JSON import source is still:

```text
%LOCALAPPDATA%\LauncherCatalog\entries.json
```

If JSON migration succeeds, backup file:
- `entries.migrated-*.json`

If JSON is malformed, backup file:
- `entries.invalid-*.json`

### Task DB

```text
%LOCALAPPDATA%\LauncherCatalog\task.db
```

Current `tasks` schema is intentionally extendable (includes `status`, `priority`, `due_date`, `notifications_enabled`, `last_reminder_key`, `created_at`, `updated_at`, `completed_at`, `sort_order`, etc.).

### Note DB

```text
%LOCALAPPDATA%\LauncherCatalog\note.db
```

Current `notes` schema stores `title`, `body`, `tags_json`, `is_favorite`, `created_at`, `updated_at`, and `last_viewed_at`.

### Automatic Backups

The app also keeps automatic SQLite backups in:

```text
%LOCALAPPDATA%\LauncherCatalog\Backups
```

Behavior:
- backup checks run automatically at app startup and continue while the app is running in the tray
- when the last successful backup is older than 2 days, the app creates one timestamped backup version folder under `Backups\Versions`
- each version folder contains the database files together using their original names such as `catalog.db`, `task.db`, and `note.db`
- only the newest 10 backup versions are kept; older version folders are deleted automatically
- restore instructions are written to `%LOCALAPPDATA%\LauncherCatalog\Backups\RESTORE.txt` so the live database path is easy to find later

### App Settings

App-level UI settings are stored in the catalog database `settings` table.

Current settings include:
- `theme`
- `quick_search_hotkey`
- `language_code`
- `ai_bridge_enabled` (AI Bridge on/off, default `true`)
- `ai_bridge_token` (Bearer token for the local AI Bridge HTTP server, stored DPAPI-encrypted)
- `ai_bridge_port` (loopback port for the AI Bridge, default `48717`)
- `ai_copilot_token` (GitHub OAuth token used for GitHub Copilot, stored DPAPI-encrypted)
- `ai_copilot_login` (GitHub login of the signed-in Copilot account)

## Startup Behavior

- The app registers itself in the current user Windows `Run` key so it starts automatically after sign-in.
- Quick search can be opened even when the main window is already visible.

## Local AI Bridge

WorkSync hosts an in-process HTTP server (ASP.NET Core / Kestrel via `AiBridgeServer`) that starts
and stops with the app, acting as a local AI gateway for the Gmail Chrome extension.

- Bound to **loopback only** (`127.0.0.1` / `::1`) on port **`48717`** (configurable via
  `settings.ai_bridge_port`).
- CORS is permissive (`Access-Control-Allow-Origin: *`, `OPTIONS -> 204`, `Max-Age 86400`) so the
  `chrome-extension://` caller can read responses.
- `POST /api/analyze` and `POST /api/draft` require `Authorization: Bearer <token>`; the token is
  generated once and stored **DPAPI-encrypted** in `settings.ai_bridge_token`. `GET /api/health` is
  open for discovery.
- Requests run through a strict **FIFO queue** (one at a time, 60 s budget each -> `504` on timeout)
  and delegate to a swappable `IAiBackend`.
- **AI provider: GitHub Copilot SDK** (`CopilotBackend`). Requests use the signed-in user's
  **GitHub Copilot subscription** (no API key). Until someone signs in, the work endpoints return **503**.
- **Sign in on the web:** the **GitHub Copilot account** card at the top of the AI Bridge tab runs
  GitHub's OAuth device flow - it opens GitHub in the browser, shows a short code (also copied to the
  clipboard), and finishes once you approve. The token is stored DPAPI-encrypted; **Sign out** forgets
  it (revoke it on GitHub under Settings > Applications if needed).
- **No setup for users:** WorkSync ships with its own GitHub OAuth App client ID
  (`CopilotBackend.ClientId`, device flow enabled, no client secret), so users only click
  **Sign in with GitHub** and authorize **WorkSync** on GitHub.
- Each request runs in a fresh Copilot session with **no tools** (email text is untrusted) and the
  session is deleted afterwards. Copilot runtime data lives under `%LOCALAPPDATA%\LauncherCatalog\Copilot`.
- An **"AI Bridge" settings tab** also lets you enable/disable the server, change the port, copy the
  endpoint URL, and show/copy/regenerate the token; a live banner shows the server status.
  Lifecycle is owned by `AiBridgeManager`.

Full design, endpoint schemas, and error contract live in [docs/AI-BRIDGE.md](docs/AI-BRIDGE.md).

## Crash Logs

Crash logs:

```text
%LOCALAPPDATA%\LauncherCatalog\Logs
```

Latest crash mirror:

```text
%LOCALAPPDATA%\LauncherCatalog\Logs\latest.log
```

## Project Structure

```text
src/LauncherCatalog/
  Models/        Catalog + task data types/enums
  Services/      SQLite repositories, launching, search, validation, dialog services
  ViewModels/    MVVM state and commands
  Views/         Windows/dialog XAML UI
  Converters/    XAML presentation converters
```

## Handoff Notes For New Chats

When starting a new chat, read this file first, then read [docs/INDEX.md](docs/INDEX.md) and the
focused docs it links. Assume:
- `Catalog`, `Tasks`, `Note`, and `AI Bridge` are the active tabs in the UI.
- Catalog data uses `catalog.db`.
- Task data uses `task.db`.
- Note data uses `note.db`.
- The app uses custom title bar/window chrome.

## Maintenance Rule

Important project rule:
- **Whenever new features, behavior, schema, commands, or architecture change, update `README.md`
  AND the affected files under `docs/` in the same change set so the documentation always stays
  current.** Any difference between the project and the docs must be fixed immediately. See
  [docs/CONVENTIONS.md](docs/CONVENTIONS.md#documentation-sync-rule).
