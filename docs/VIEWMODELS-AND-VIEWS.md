# ViewModels & Views

Back to [INDEX.md](INDEX.md).

## MVVM building blocks ([../src/LauncherCatalog/ViewModels](../src/LauncherCatalog/ViewModels))

| File | Purpose |
|------|---------|
| `ObservableObject` | `INotifyPropertyChanged` base (`SetField`, `OnPropertyChanged`). |
| `RelayCommand` | Synchronous `ICommand`. |
| `AsyncRelayCommand` | Async `ICommand` with optional concurrent execution + `CanExecute`. |

## ViewModels

| ViewModel | Drives | Notes |
|-----------|--------|-------|
| `MainViewModel` | Catalog + Tasks + Note tabs | Largest VM. Holds `EntriesView`/`NotesView` (`ICollectionView` with filter + sort), debounced search (~220 ms), task tree (`_taskRoots`/`_taskLookup`), all the catalog/task/note commands, language + theme toggles. For Notes it exposes the live editor fields that back the in-place note editor while keeping `NoteEditorBody` as plain text for persistence/search. Constructed in `MainWindow` with injected services. |
| `AiBridgeViewModel` | AI Bridge tab | Surfaces the shared `AiBridgeManager` state (enabled, port, token, status, GitHub Copilot account). Commands: `AccountCommand` (sign in with GitHub / sign out), `CancelSignInCommand`, toggle token visibility, copy token/endpoint, regenerate token, apply port. `AccountMessage` shows sign-in feedback inside the account card. During sign-in `DeviceCode` holds the device-flow code (auto-copied to the clipboard). Mutations delegate to the manager so there is one listener app-wide. |
| `EntryEditorViewModel` | Entry add/edit dialog | Validates and edits a `CatalogEntry`. |
| `TaskEditorViewModel` | Task create/edit dialog | Create mode locks `Status=Todo`; edit mode unlocks all fields. |

`MainViewModel` and `AiBridgeViewModel` subscribe to `LocalizationService.CultureChanged` to
refresh localized strings at runtime.

## Views ([../src/LauncherCatalog/Views](../src/LauncherCatalog/Views))

| View | Purpose |
|------|---------|
| `EntryEditorDialog` | Add/edit catalog entry. |
| `TaskEditorDialog` | Create/edit task (Material Design popup calendar for due date). |
| `AiBridgeView` | AI Bridge settings tab: **GitHub Copilot account card at the top** (status dot + "Signed in as", Sign in/Sign out button, device-code panel with Cancel while sign-in is pending), then status banner, enable switch, port + endpoint, token Show/Copy/Regenerate. Self-instantiates `AiBridgeViewModel`. |
| `QuickSearchWindow` | Global-hotkey quick launcher. |
| `HotkeySettingsDialog` | Configure the quick-search hotkey. |
| `SettingsWindow` | Dedicated settings dialog. Left category list (currently one **General** tab, extensible) + right detail panel. General hosts theme (dark/light), language, and the quick-search shortcut. Binds to the shared `MainViewModel` so theme/language apply live; hotkey changes go through an injected `Func<string, Task<bool>>` callback (OS registration needs the main window handle). **OK** only closes. |
| `RestoreBackupDialog` | Browse/restore a backup version. |

Top-level shell:

| File | Purpose |
|------|---------|
| [../src/LauncherCatalog/MainWindow.xaml](../src/LauncherCatalog/MainWindow.xaml) / `.xaml.cs` | Custom title bar, tab host, tray icon, hotkey + window-chrome logic, service composition root. The Note tab uses a split list/editor workspace; the editable note body rebuilds inline hyperlinks after a one-second typing pause so note URLs stay clickable without flickering after every keystroke. The transparent caption grid lets the whole empty top bar surface participate in window dragging. |
| [../src/LauncherCatalog/App.xaml](../src/LauncherCatalog/App.xaml) / `.xaml.cs` | App resources, single-instance, crash handlers, lifecycle. |

## Converters ([../src/LauncherCatalog/Converters](../src/LauncherCatalog/Converters))

XAML `IValueConverter` helpers, e.g. `BooleanToOpacityConverter`, `EntryTypeToGlyphConverter`,
`FavoriteGlyphConverter`, `MissingStatusBrushConverter`, `MissingStatusTextConverter`,
`NullToVisibilityConverter`, `StringNullOrEmptyToVisibilityConverter`, `TaskDepthToIndentConverter`.

## Behaviors ([../src/LauncherCatalog/Behaviors](../src/LauncherCatalog/Behaviors))

- `TextLinkifier` �? attached property `TextLinkifier.BoundText` on a read-only `RichTextBox`. Renders a
  plain-text string and turns `http`/`https` URLs into clickable blue hyperlinks (accent brush) that open
  in the OS default browser via `Process.Start(UseShellExecute)`; keeps the text selectable/copyable and
  preserves line breaks. Trailing sentence punctuation is trimmed off matched URLs.

> UI/VM structure changes **must** update this file and `README.md` in the same change set.
