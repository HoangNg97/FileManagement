# WorkSync

WorkSync is a Windows desktop app built with C#, WPF, and .NET 8.

It currently has 5 main tabs:
- `Catalog`: searchable launcher list for apps/folders.
- `Tasks`: task tracker with SQLite-backed create/edit/complete flow.
- `Note`: local note workspace with search, favorites, tags, live editing, and preview.
- `History Management`: SQLite-backed history-file management with curved route/map event visualization.
- `Issues`: local issue conversation history (Jira-comment style) with per-issue message threads.

## Documentation Map (read this first)

This README is the user-facing overview. For a complete technical understanding **without reading
the whole codebase**, follow the documentation map:

- **Start here:** [docs/INDEX.md](docs/INDEX.md) â€? links to every doc in read order.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) â€? tech stack, layering, lifecycle, app-wide behaviors.
- [docs/DATA-MODEL.md](docs/DATA-MODEL.md) â€? SQLite schemas + models per feature.
- [docs/SERVICES.md](docs/SERVICES.md) â€? what every service does.
- [docs/VIEWMODELS-AND-VIEWS.md](docs/VIEWMODELS-AND-VIEWS.md) â€? MVVM map (ViewModels, Views, Converters).
- [docs/CONVENTIONS.md](docs/CONVENTIONS.md) â€? coding/localization conventions + the doc-sync rule.
- [docs/AI-BRIDGE.md](docs/AI-BRIDGE.md) â€? local AI gateway (HTTP contract, Gemini automation, settings).

> [!IMPORTANT]
> **Mandatory reading rule (for AI assistants and new contributors):** reading this README alone is
> **not enough**. Whenever you are asked to *"read README.md"* (or start a new chat on this project),
> you **must also read every file in the documentation map above** before doing any work â€? that is:
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

## Publish (Single EXE)

```powershell
dotnet publish .\src\LauncherCatalog\LauncherCatalog.csproj -c Release -r win-x64 --self-contained true -o .\dist\WorkSync-win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output folder:

```text
dist\WorkSync-win-x64
```

## Current UI/UX Notes

- Custom window chrome is enabled (`WindowStyle=None`) with an in-app title bar.
- The title bar includes:
  - language selector with runtime switching
  - theme toggle (dark/light)
  - a **chat** button that opens the in-app **Ask Gemini** panel (see below)
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

## History Management Feature

History data is persisted in `history.db` and shown in the `History Management` tab.

A `HistoryFile` is a database record used for organizing history events. It is not a real physical file on disk.

Current history-file capabilities:
- Create, rename/edit, delete, favorite/unfavorite, and search history files
- Search history files by name or tag
- Favorite history files are sorted to the top
- Right-click a history file to open its context menu actions

Current history-event capabilities:
- Create, edit, and delete events inside the currently opened history file
- Store event date, free-text note, created timestamp, and updated timestamp
- Allow multiple events on the same date
- Search events only inside the currently opened history file
- Selecting an event search result navigates to its month/year and highlights that event once
- Pressing Enter in the event search field temporarily redraws the route map to show matching events only

Visualization behavior:
- The opened history file is displayed as a scrollable curved route/path timeline instead of a table or standard list
- Event points are drawn along one continuous route and show event date plus note text
- Month/year selectors control the viewed event range
- The month selector includes `All`, which shows all events in the selected year
- Empty days are hidden; if the selected view has no events, an empty state is shown
- Route controls are overlaid on top of the visualization
- A loading overlay is shown while the route visualization refreshes for larger event sets

Export behavior:
- Export the selected month/current year view to `.xlsx`
- Export all events in the current history file to `.xlsx`

## Issues Feature

Issue data is persisted in `issue.db` and shown in the `Issues` tab. It is a local, Jira-comment-style
history store: each **issue** groups a thread of **messages**. Nothing is sent anywhere â€? messages are
local history records, so the primary action is **Add** (not "Send").

Layout is two panels:
- **Left issue list**: each row shows the issue title, a latest-message preview (`Author: text`), and a
  relative "time ago". Issues are sorted by latest activity; a new/edited/removed message re-sorts the
  list so the most recent issue moves to the top.
- **Right message thread**: an optional **note** panel at the top (shown read-only when the issue has a
  note), then message cards ordered newestâ†’oldest (newest on top), each with a compact `Author Â· date`
  header (plus an `edited` marker when applicable) and inline **Edit**/**Remove** actions. A fixed
  **Add message** box sits at the bottom. URLs (`http`/`https`) in message text and the note render as
  clickable blue hyperlinks that open in the default browser; the text stays selectable/copyable.

Current capabilities:
- Search across issue titles, issue notes, message content, and author names (the issue list filters live).
- Create a new issue via **+ New Issue** (title required; optional **note** that stays with the issue).
- **Edit an issue** (right-click â†? **Edit issue**): change its title and note.
- **Delete an issue** (right-click â†? **Delete issue**): removes the issue and all of its messages after an
  "action cannot be undone" confirmation.
- Add a message to the selected issue with message body + author + created date fields. Author defaults
  to the current Windows user and created date defaults to now (live-updating while the app stays open
  until you pick a custom date). Date fields are shown as date-only text (no hour/minute).
- Edit a message inline (Cancel/Save; empty content is rejected). In edit mode the message body,
  author, and created date are all editable; save records an edited marker and re-applies newest-first
  sorting by created date, then auto-scrolls the thread to the edited message in its new position.
  Remove still requires an "action cannot be undone" confirmation.
- Empty states for "no issues yet" and "no messages yet".
- **Export to Excel**: right-click the issue list for **Export to Excel sheet**. Select one issue (plain
  click) or several (Shift/Ctrl multi-select) to export them into a single styled `.xlsx`. The layout is
  vertical (Jira-comment style): each issue is a two-column block with a title banner, note /
  created-date / message-count metadata, a "Conversation history" band, and every message as a shaded
  header row followed by a wrapped body row. Multiple issues are placed side by side and
  divided by a thick vertical border; the top five rows (title + metadata + section band) are frozen. The
  file is generated as raw OOXML with no external Excel dependency.
- **Import conversation**: right-click the issue list for **Import conversation** to bulk-add a long
  conversation from a JSON file instead of typing each message. The dialog shows a copyable prompt (for
  generating a correctly-shaped JSON with any AI assistant), an **Open** button to pick the `.json`, and
  an **Import** button. The file is `{ "messages": [ { "author", "date" (ISO-8601, optional), "content" } ] }`
  (a bare top-level array is also accepted). Imported messages merge into the selected issue, exact
  duplicates (same author + date + content) are skipped, and the whole thread is re-sorted by date
  (newest on top) so imported messages land in the correct chronological position.

Add-message box behavior:
- Starts compact and auto-expands vertically as you type, capped at ~50% of the right panel height,
  after which it scrolls internally.
- Growing the box shrinks the message list above it (the list scrolls); the box never floats over or
  hides existing messages.
- Metadata fields in the add area are arranged in one horizontal row (`Message date` + picker,
  `Author` + text field) to preserve vertical space.

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

### History DB

```text
%LOCALAPPDATA%\LauncherCatalog\history.db
```

Current history schema stores:
- `history_files`: `name`, `tags_json`, `is_favorite`, `created_at`, and `updated_at`
- `history_events`: owning `history_file_id`, `event_date`, `note`, `created_at`, and `updated_at`

### Issue DB

```text
%LOCALAPPDATA%\LauncherCatalog\issue.db
```

Current issue schema stores:
- `issues`: `title`, `note`, `created_at`, and `updated_at`
- `messages`: owning `issue_id` (FK, cascade delete), `created_by`, `created_at`, `content`, `modified_at`, and `is_edited`

### Automatic Backups

The app also keeps automatic SQLite backups in:

```text
%LOCALAPPDATA%\LauncherCatalog\Backups
```

Behavior:
- backup checks run automatically at app startup and continue while the app is running in the tray
- when the last successful backup is older than 2 days, the app creates one timestamped backup version folder under `Backups\Versions`
- each version folder contains the database files together using their original names such as `catalog.db`, `task.db`, `note.db`, `issue.db`, and `history.db`
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

## Startup Behavior

- The app registers itself in the current user Windows `Run` key so it starts automatically after sign-in.
- Quick search can be opened even when the main window is already visible.

## In-App Chat (Ask Gemini)

Beside the settings gear in the title bar is a **chat** button that opens an in-app Gemini chat
panel (bubble-style scale/fade animation from the button, like tapping an Android notification
bubble). The panel is tab-aware:

- The chat sends the **currently focused tab's data** as **inline JSON** inside the Gemini prompt
  on the first message, so questions like *"which apps or folders relate to Omron?"* work against
  real data. Supported tabs: **Catalog**, **Tasks**, **Note**, **History Management**, **Issues**.
  The **AI Bridge** tab does not participate.
- **Auto-chunking for large tabs.** If the tab's JSON is short enough it rides along with the
  question in a single Gemini turn. When it exceeds the per-turn budget the chat panel splits the
  JSON at line boundaries and sends the parts across multiple sequential turns in the **same**
  Gemini conversation â€? intermediate turns tell Gemini "part *i* of *N*, acknowledge with 'OK' and
  wait", and the final turn includes the last chunk plus your actual question. The pending bubble
  updates with "Sending data (*i*/*N*)â€¦" so the progress is visible.
- Follow-up messages in the same conversation reuse Gemini's memory of the already-sent data.
  Switching tabs mid-conversation re-sends the new tab's JSON automatically.
- The chat runs on the same **Local AI Bridge** infrastructure and reuses the **same Gemini
  browser tab** as the Gmail Chrome extension â€? no second browser or tab is opened. Whenever the
  chat and the Gmail bridge take turns using Gemini, the driver clicks Gemini's **New chat** button
  so their conversations never mix.
- **Errors** (AI Bridge disabled, port in use, not signed in to Gemini, or the request failed) are
  shown as an inline banner above the input and nothing is sent to Gemini until they are fixed.
  Backend errors include a short trace id in `[brackets]` that also appears in
  `%LOCALAPPDATA%\LauncherCatalog\Logs\latest.log` for correlation.
- The chat history in the panel is **in-memory only** â€? bubbles reset when the app is closed. The
  panel has a **New conversation** button that both clears the bubbles and forces the next message
  to start a fresh Gemini chat.
- The panel **closes on click-away** (mouse click outside the panel and outside the chat toggle
  button) and also when the app loses focus, in addition to the explicit close button.
- Enter sends; Shift+Enter adds a newline.

## Local AI Bridge

WorkSync hosts an in-process HTTP server (ASP.NET Core / Kestrel via `AiBridgeServer`) that starts
and stops with the app, acting as a local AI gateway for the Gmail Chrome extension.

- Bound to **loopback only** (`127.0.0.1` / `::1`) on port **`48717`** (configurable via
  `settings.ai_bridge_port`).
- CORS is permissive (`Access-Control-Allow-Origin: *`, `OPTIONS â†? 204`, `Max-Age 86400`) so the
  `chrome-extension://` caller can read responses.
- `POST /api/analyze` and `POST /api/draft` require `Authorization: Bearer <token>`; the token is
  generated once and stored **DPAPI-encrypted** in `settings.ai_bridge_token`. `GET /api/health` is
  open for discovery.
- Requests run through a strict **FIFO queue** (one at a time, 60 s budget each â†? `504` on timeout)
  and delegate to a swappable `IAiBackend`. The live backend is **`GeminiWebDriver`**, which drives
  the **Gemini website** with Playwright (no API key).
- An **"AI Bridge" settings tab** lets you enable/disable the server, change the port, copy the
  endpoint URL, show/copy/regenerate the token, and **sign in to Gemini**; live banners show the
  server status and the Gemini sign-in state. Lifecycle is owned by `AiBridgeManager`.
- **Gemini automation:** click **"Log in to Gemini"** to open a real (non-headless) browser against a
  persistent profile and sign in once (2FA handled by you). It prefers your installed **Edge/Chrome**
  and falls back to a one-time Chromium download. Until you are signed in, the work endpoints return
  **503**. Each request opens a fresh Gemini conversation, submits the prompt, scrapes the answer, and
  `GeminiOutputSanitizer` extracts the JSON object (unparseable â†? `502`).
- **Silent sign-out notification:** if Google signs the session out, the next request detects it and
  raises a **Windows tray notification** ("Gemini sign-in required"). It persists in the notification
  center (not auto-dismissed) so you notice even while the app is minimized; clicking it restores the
  window and opens the AI Bridge tab to sign in again.
- **Status â€? all three stages built.** Selectors target the live Gemini DOM and may need tuning if
  Google changes the page. `StubAiBackend` remains in the codebase (unused) for offline testing.

Full design, endpoint schemas, error contract, and the stage checklist live in
[docs/AI-BRIDGE.md](docs/AI-BRIDGE.md).

## Crash Logs

Crash logs:

```text
%LOCALAPPDATA%\LauncherCatalog\Logs
```

Latest crash mirror:

```text
%LOCALAPPDATA%\LauncherCatalog\Logs\latest.log
```

## Gemini OAuth Setup

Current Gemini setup status:
- Google OAuth desktop-app sign-in flow is wired through `GoogleWebAuthorizationBroker`
- tokens are cached locally so login should persist across app restarts
- `client_secret.json` is embedded into the app assembly for single-EXE-friendly distribution

Required file for local auth:

```text
client_secret.json
```

Current project setup:
- development source file lives at `src/LauncherCatalog/client_secret.json`
- the build embeds it into the executable assembly
- runtime auth no longer depends on a sidecar JSON file next to the EXE

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
- `Catalog`, `Tasks`, `Note`, `History Management`, and `Issues` are all active tabs in the UI.
- Catalog data uses `catalog.db`.
- Task data uses `task.db`.
- Note data uses `note.db`.
- History data uses `history.db`.
- The app uses custom title bar/window chrome.

## Maintenance Rule

Important project rule:
- **Whenever new features, behavior, schema, commands, or architecture change, update `README.md`
  AND the affected files under `docs/` in the same change set so the documentation always stays
  current.** Any difference between the project and the docs must be fixed immediately. See
  [docs/CONVENTIONS.md](docs/CONVENTIONS.md#documentation-sync-rule).
