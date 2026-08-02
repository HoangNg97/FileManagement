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
| `HistoryManagementViewModel` | History Management tab | History file/event CRUD, year/month selectors (`HistoryMonthOption`, `All` month), event search + highlight, `.xlsx` export, route-map refresh + loading overlay. Has a parameterless ctor (defaults `HistoryRepository`/`HistoryExcelExportService`). |
| `AiBridgeViewModel` | AI Bridge tab | Surfaces the shared `AiBridgeManager` state (enabled, port, token, status, Gemini sign-in). Commands: toggle token visibility, copy token/endpoint, regenerate token, apply port, **log in to Gemini** (drives `GeminiWebDriver`). Mutations delegate to the manager so there is one listener app-wide. |
| `ChatViewModel` | Ask Gemini chat panel (title-bar chat button) | In-memory `Messages` collection (`ChatMessage`), `SendCommand`/`NewConversationCommand`, `HeaderTitle`/`InputPlaceholder`/`EmptyStateText` that reflect the currently-focused tab (via a `Func<ChatContextTab>` injected by `MainWindow`), `ErrorText`/`HasError` for the inline error banner. On send: pre-checks `AiBridgeManager` (disabled/port-in-use/not-signed-in) and refuses locally when broken; otherwise serializes the active tab's DB rows through `ChatContextExporter.BuildAsync` and calls `AiBridgeManager.ChatAsync(prompt)` with the JSON stitched inline. When the JSON is too large to fit in one turn it is split at line boundaries and sent as `Chat.SendingContext` progress-labeled sub-messages inside the same Gemini conversation, with the actual question riding on the final chunk. Re-sends the JSON whenever the user switches tabs; follow-up messages within the same tab go text-only so the conversation feels natural. |
| `IssuesViewModel` | Issues tab | Owns the issue list (`Issues` observable collection), search filter (title/note/message content/author) + latest-activity sort, `SelectedIssue`, and the issue create/edit/delete + add/edit/remove-message commands (`IssueRepository`, `issue.db`). Issues carry a free-text `Note` (no issue-level author); message threads are newest-first (newest on top). New-message metadata is explicit: body + author + created date (default author = current Windows user, default date = now with live clock updates until user overrides). Inline edit mode edits body, author, and created date; save persists all three fields, re-sorts newest-first by created date, and raises `ScrollToMessageRequested` so the view jumps to the edited message in its new position. Date display text is date-only (no hour/minute) while the stored `DateTime` remains intact for ordering/persistence. Also exposes `ExportSelectedIssuesCommand` (styled `.xlsx` via `IssueExcelExportService`; the view pushes multi-selection through `UpdateSelectedIssues` since WPF `SelectedItems` is not bindable, falling back to the active issue for a plain right-click) and `ImportConversationCommand` (`ImportConversationAsync` via `IssueConversationImportService`: merges a JSON conversation into the selected issue, skips exact duplicates, re-sorts newest-first). Dialog-driven actions (new issue, edit issue, import, remove message/issue confirm), empty-message validation, and export/import result messages are exposed as events (`NewIssueRequested`, `EditIssueRequested`, `RemoveIssueRequested`, `ImportConversationRequested`, `RemoveMessageRequested`, `ValidationFailed`, `ScrollToNewestRequested`, `ScrollToMessageRequested`, `ExportNotification`, `ImportNotification`) so the view hosts the windows/dialogs. Self-instantiated by `IssuesView`. |
| `EntryEditorViewModel` | Entry add/edit dialog | Validates and edits a `CatalogEntry`. |
| `TaskEditorViewModel` | Task create/edit dialog | Create mode locks `Status=Todo`; edit mode unlocks all fields. |

Both `MainViewModel` and `HistoryManagementViewModel` subscribe to
`LocalizationService.CultureChanged` to refresh localized strings at runtime. `IssuesViewModel` and
`AiBridgeViewModel` do the same for their tabs.

## Views ([../src/LauncherCatalog/Views](../src/LauncherCatalog/Views))

| View | Purpose |
|------|---------|
| `EntryEditorDialog` | Add/edit catalog entry. |
| `TaskEditorDialog` | Create/edit task (Material Design popup calendar for due date). |
| `HistoryManagementView` | History tab content (route/timeline visualization host). |
| `AiBridgeView` | AI Bridge settings tab: enable switch, port + endpoint, token Show/Copy/Regenerate, status banner, Gemini sign-in (functional login + indicator). Self-instantiates `AiBridgeViewModel`. |
| `ChatPanel` | In-app Gemini chat overlay opened by the title-bar chat button. UserControl hosted by `MainWindow` at top-right; `ShowAnimated`/`HideAnimated` scale + fade from the top-right origin like an Android bubble tap. Message list (user bubbles right, assistant bubbles left, URLs linkified via `Behaviors/TextLinkifier`), inline error banner above the input, autoscroll to the newest message, Enter-sends / Shift+Enter-newline, header shows the active tab plus **New conversation** and **Close** buttons. The overlay also closes on **click-away** (a `PreviewMouseDown` handler on `MainWindow` walks the visual tree from the click's original source and hides the panel unless the click lands inside the panel or on `ChatButton`) and on window `Deactivated`. `MainWindow` constructs the `ChatViewModel` and attaches it via `AttachViewModel`. |
| `IssuesView` | Issues tab content: two-panel layout (left issue list, right side = optional read-only note panel + message thread newest-on-top + auto-expanding "Add message" box capped at half the panel height). Add-message footer includes a compact horizontal metadata row (`Message date` label + picker, `Author` label + text field) on the left and Add on the right. Inline edit mode in each message card also exposes editable date + author controls (same popup-calendar interaction pattern as task due-date selection) plus content. Message text and the note render URLs as clickable hyperlinks via `Behaviors/TextLinkifier` (read-only `RichTextBox`, still selectable). The issue list uses `Extended` selection (Shift/Ctrl to multi-select) with a right-click context menu ("Edit issue", "Delete issue", "Export to Excel sheet", "Import conversation"); right-click selects the clicked row unless it is already part of a multi-selection. Self-instantiates `IssuesViewModel`; hosts the new/edit-issue dialog, import-conversation dialog, remove-message/issue confirms, empty-message validation prompts, and the export/import result prompts; scrolls the thread to the newest (top) message and can also jump to a just-edited message after resorting. |
| `NewIssueDialog` | Create or edit an issue (title required, optional note). Reused for both modes: the parameterless constructor is "new", the `(title, note)` constructor is "edit" (adjusts the header + primary button and pre-fills the fields). Exposes `IssueTitle` and `Note`. |
| `ImportConversationDialog` | Import a conversation into the selected issue: a copyable read-only prompt (for generating a valid JSON), an **Open** button to pick the `.json`, a read-only selected-file label, and an **Import** button (enabled once a file is chosen). Exposes `SelectedFilePath`. |
| `HistoryFileEditorDialog` | Create/rename history file. |
| `HistoryEventEditorDialog` | Create/edit history event. |
| `HistoryRouteMap.cs` | Custom rendering of the curved route/timeline of events. |
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

- `TextLinkifier` â€? attached property `TextLinkifier.BoundText` on a read-only `RichTextBox`. Renders a
  plain-text string and turns `http`/`https` URLs into clickable blue hyperlinks (accent brush) that open
  in the OS default browser via `Process.Start(UseShellExecute)`; keeps the text selectable/copyable and
  preserves line breaks. Trailing sentence punctuation is trimmed off matched URLs.

> UI/VM structure changes **must** update this file and `README.md` in the same change set.
