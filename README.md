# Launcher Catalog

Launcher Catalog is a Windows desktop app for saving applications and folders into one searchable launcher list. It is built with C#, WPF, and .NET 8, and is intended to be developed from Visual Studio Code.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK with Windows Desktop support
- Visual Studio Code with the C# extension

The project uses the `Microsoft.Data.Sqlite` NuGet package for local SQLite storage.

Everything integration is optional. The main voidtools app is `Everything.exe`, but in-app result listing requires Everything's command-line query tool, `es.exe`. If `es.exe` is not found, catalog search continues to work normally.

## Run From VS Code

Open this folder in VS Code, then run one of the included tasks:

- `build`
- `run`
- `publish win-x64`

Equivalent terminal commands:

```powershell
dotnet build .\FileLauncherCatalog.sln
dotnet run --project .\src\LauncherCatalog\LauncherCatalog.csproj
```

## Publish

To create a runnable Windows build:

```powershell
.\scripts\publish-windows.ps1
```

The executable is written to:

```text
dist\LauncherCatalog-win-x64
```

## What It Does

- Add application/file entries from any file path. Windows opens them the same way it would when double-clicked.
- Add folder entries from a folder picker.
- Edit display name, path, tags, and note.
- Search the saved catalog by display name, file name, folder name, full path, tags, and note.
- Optionally include Everything filesystem results below saved catalog matches.
- Open apps and folders from the list.
- Reveal saved locations in File Explorer.
- Copy full paths.
- Delete catalog records without deleting files from disk.
- Detect missing paths and show a clear status badge.
- Show only folder names in the main list while exposing full paths through tooltips and the details panel.

## Storage

Entries and app settings, including the last selected light/dark theme, are stored in a local SQLite database in the current user's local app data folder:

```text
%LOCALAPPDATA%\LauncherCatalog\entries.db
```

The app does not require admin rights and does not require a separate SQLite installation. Published single-file builds bundle the SQLite runtime with the app.

Older builds stored data in:

```text
%LOCALAPPDATA%\LauncherCatalog\entries.json
```

When the new SQLite build starts for the first time, it imports that JSON file into `entries.db` automatically. After a successful import, it keeps a timestamped compatibility backup named `entries.migrated-*.json`. If the legacy JSON file is malformed, the app moves it to an `entries.invalid-*.json` backup and starts with an empty SQLite catalog.

## Crash Logs

If the app hits an unexpected fatal error, it writes a log file here:

```text
%LOCALAPPDATA%\LauncherCatalog\Logs
```

The newest crash is also copied to:

```text
%LOCALAPPDATA%\LauncherCatalog\Logs\latest.log
```

Legacy JSON import supports a file with a `settings` object and an `entries` array. Example entry inside `entries`:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "type": "App",
  "displayName": "Photoshop",
  "exePath": "D:\\Tools\\Adobe\\Photoshop\\Photoshop.exe",
  "exeName": "Photoshop.exe",
  "folderName": "Photoshop",
  "folderPath": "D:\\Tools\\Adobe\\Photoshop",
  "tags": ["design", "image"],
  "note": "Main editing app",
  "dateAdded": "2026-04-21T10:00:00",
  "lastOpened": null,
  "isMissing": false,
  "isFavorite": false
}
```

## Project Structure

```text
src/LauncherCatalog/
  Models/        Entry data types
  Services/      SQLite repository, launching, search, validation, dialogs
  ViewModels/    MVVM state and commands
  Views/         Add/edit dialog
  Converters/    XAML presentation converters
```

## Future Features

Good next steps are import/export, drag and drop, custom extracted icons, dark mode, and Start Menu import. The current service boundaries are meant to make those additions local rather than invasive.
