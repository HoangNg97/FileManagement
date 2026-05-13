# Launcher Catalog

Launcher Catalog is a Windows desktop app built with C#, WPF, and .NET 8.

It currently has 3 main tabs:
- `Catalog`: searchable launcher list for apps/folders.
- `Tasks`: task tracker with SQLite-backed create/edit/complete flow.
- `Note`: local note workspace with search, favorites, tags, live editing, and preview.

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
dotnet publish .\src\LauncherCatalog\LauncherCatalog.csproj -c Release -r win-x64 --self-contained true -o .\dist\LauncherCatalog-win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output folder:

```text
dist\LauncherCatalog-win-x64
```

## Current UI/UX Notes

- Custom window chrome is enabled (`WindowStyle=None`) with an in-app title bar.
- The title bar includes:
  - language selector with runtime switching
  - theme toggle (dark/light)
  - minimize / maximize-restore / close buttons
- Title bar supports drag and double-click maximize/restore.
- Task due-date selection uses a text-first field that opens a Material Design popup calendar and follows both light and dark mode.
- Catalog/task filter dropdowns use padded left-aligned labels, and task due-date/action controls use compact sizing to preserve table readability.
- UI text is centralized for localization and can switch languages without restarting the app.

## Localization

- Current built-in languages:
  - `English`
  - `Japanese`
- Language changes are applied at runtime from the title bar language dropdown.
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
- Simple read preview with checklist (`- [ ]`, `- [x]`) and heading support
- Split note workspace with list, editor, and preview/details panels

## Storage

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

### App Settings

App-level UI settings are stored in the catalog database `settings` table.

Current settings include:
- `theme`
- `quick_search_hotkey`
- `language_code`

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

When starting a new chat, read this file first, then assume:
- `Catalog`, `Tasks`, and `Note` are all active tabs in the UI.
- Catalog data uses `catalog.db`.
- Task data uses `task.db`.
- Note data uses `note.db`.
- The app uses custom title bar/window chrome.

## Maintenance Rule

Important project rule:
- **Whenever new features, behavior, schema, commands, or architecture change, update `README.md` in the same change set so this document always stays current.**
