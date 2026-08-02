# Data Model

Back to [INDEX.md](INDEX.md).

All databases are SQLite files under `%LOCALAPPDATA%\LauncherCatalog`. Timestamps are stored as
ISO-8601 (`"O"` round-trip) strings. GUIDs are stored as text. Booleans are stored as `INTEGER`
(0/1). Tag lists are stored as JSON arrays in `tags_json`.

---

## Catalog — `catalog.db`

Repository: [../src/LauncherCatalog/Services/EntryRepository.cs](../src/LauncherCatalog/Services/EntryRepository.cs)
Model: [../src/LauncherCatalog/Models/CatalogEntry.cs](../src/LauncherCatalog/Models/CatalogEntry.cs)

### `entries`

| Column | Type | Notes |
|--------|------|-------|
| `id` | TEXT PK | GUID |
| `type` | TEXT | `EntryType` (App / Folder) |
| `display_name` | TEXT | |
| `exe_path`, `exe_name` | TEXT NULL | app entries |
| `folder_name`, `folder_path` | TEXT | |
| `tags_json` | TEXT | JSON string array |
| `note` | TEXT NULL | |
| `date_added` | TEXT | |
| `last_opened` | TEXT NULL | |
| `is_missing` | INTEGER | path-missing flag |
| `is_favorite` | INTEGER | |

- **FTS:** `entries_fts` (FTS5, `unicode61`) mirrors searchable columns for fast text search,
  with a LIKE fallback. Search returns matching entry IDs (`SearchEntryIdsAsync`).
- Indexes on type, missing, favorite, date_added, last_opened, folder_path, exe_path.
- `settings` table (key/value) stores app-level UI settings: `theme`, `quick_search_hotkey`,
  `language_code`, `ai_bridge_enabled` (AI Bridge on/off, default `true`), `ai_bridge_token`
  (AI Bridge Bearer token, **DPAPI-encrypted**, `DPAPI:` prefix), `ai_bridge_port` (AI Bridge
  loopback port, default `48717`). `meta` table tracks migration flags (e.g.
  `json_migration_completed`).

### Migrations
- Legacy `entries.db` → renamed to `catalog.db` on startup.
- Legacy `entries.json` → imported once; backups written as `entries.migrated-*.json` (success) or
  `entries.invalid-*.json` (malformed).

---

## Tasks — `task.db`

Repository: [../src/LauncherCatalog/Services/TaskRepository.cs](../src/LauncherCatalog/Services/TaskRepository.cs)
Model: [../src/LauncherCatalog/Models/TaskItem.cs](../src/LauncherCatalog/Models/TaskItem.cs)
Enums: `TaskStatus`, `TaskPriority`.

### `tasks`

| Column | Type | Notes |
|--------|------|-------|
| `id` | TEXT PK | GUID |
| `title` | TEXT | |
| `description` | TEXT NULL | |
| `due_date` | TEXT NULL | |
| `status` | TEXT | `TaskStatus` (Todo / On-going / Done) |
| `priority` | TEXT | `TaskPriority` (Low / Normal / Urgent) |
| `is_completed` | INTEGER | |
| `notifications_enabled` | INTEGER | reminder bell toggle |
| `last_reminder_key` | TEXT NULL | dedupe key for reminders |
| `completed_at` | TEXT NULL | |
| `created_at`, `updated_at` | TEXT | |
| `sort_order` | INTEGER | manual ordering |
| `parent_task_id` | TEXT NULL | subtask hierarchy |

- `task_meta` (key/value) for metadata.
- Subtasks via self-reference `parent_task_id` (index `idx_tasks_parent_task_id`).
- Newer columns (`notifications_enabled`, `last_reminder_key`, `parent_task_id`) are added at
  runtime via `EnsureColumnAsync` for forward-compatible migration.
- Reminder scheduling rules live in `TaskReminderService` (see [SERVICES.md](SERVICES.md)).

---

## Note — `note.db`

Repository: [../src/LauncherCatalog/Services/NoteRepository.cs](../src/LauncherCatalog/Services/NoteRepository.cs)
Model: [../src/LauncherCatalog/Models/NoteItem.cs](../src/LauncherCatalog/Models/NoteItem.cs)
Related: `NotePreviewLine` (rendered preview lines), `NoteFilterMode` enum.

### `notes`

| Column | Type | Notes |
|--------|------|-------|
| `id` | TEXT PK | GUID |
| `title` | TEXT | |
| `body` | TEXT | markdown-ish (headings, `- [ ]`/`- [x]` checklist) |
| `tags_json` | TEXT | JSON string array |
| `is_favorite` | INTEGER | |
| `created_at`, `updated_at` | TEXT | |
| `last_viewed_at` | TEXT NULL | drives Recent sorting |

Indexed on favorite, updated_at, last_viewed_at, title.

---

## History Management — `history.db`

Repository: [../src/LauncherCatalog/Services/HistoryRepository.cs](../src/LauncherCatalog/Services/HistoryRepository.cs)
Models: `HistoryFileItem`, `HistoryEventItem`, `HistoryMonthOption`.

A *history file* is a database record (a logical grouping), **not** a real file on disk.

### `history_files`

| Column | Type | Notes |
|--------|------|-------|
| `id` | TEXT PK | GUID |
| `name` | TEXT | |
| `created_date` | TEXT | |
| `tags_json` | TEXT | JSON string array |
| `is_favorite` | INTEGER | favorites sort to top |
| `created_at`, `updated_at` | TEXT | |

### `history_events`

| Column | Type | Notes |
|--------|------|-------|
| `id` | TEXT PK | GUID |
| `history_file_id` | TEXT | FK → `history_files(id)` `ON DELETE CASCADE` |
| `event_date` | TEXT | multiple events per date allowed |
| `note` | TEXT | free text |
| `created_at`, `updated_at` | TEXT | |

Index `idx_history_events_file_date` on (`history_file_id`, `event_date`).

Events render as a curved route/timeline (`Views/HistoryRouteMap.cs`). Export to `.xlsx` via
`HistoryExcelExportService` (selected month/year view, or all events).

---

## Issues — `issue.db`

Repository: [../src/LauncherCatalog/Services/IssueRepository.cs](../src/LauncherCatalog/Services/IssueRepository.cs)
Models: [../src/LauncherCatalog/Models/Issue.cs](../src/LauncherCatalog/Models/Issue.cs),
[../src/LauncherCatalog/Models/IssueMessage.cs](../src/LauncherCatalog/Models/IssueMessage.cs).
Helper: `RelativeTimeFormatter` (localized "time ago" for the issue list).

A local, Jira-comment-style history store: each *issue* groups a thread of *messages*.

### `issues`

| Column | Type | Notes |
|--------|------|-------|
| `id` | TEXT PK | GUID |
| `title` | TEXT | |
| `note` | TEXT | free-text note that stays with the issue (edited via the issue dialog) |
| `created_at` | TEXT | |
| `updated_at` | TEXT | latest activity; drives list sort (newest first) |

### `messages`

| Column | Type | Notes |
|--------|------|-------|
| `id` | TEXT PK | GUID |
| `issue_id` | TEXT | FK → `issues(id)` `ON DELETE CASCADE` |
| `created_by` | TEXT | author |
| `created_at` | TEXT | drives newest→oldest thread order (newest on top) |
| `content` | TEXT | message text |
| `modified_at` | TEXT NULL | set when edited |
| `is_edited` | INTEGER | edited marker |

Indexes: `idx_issues_updated_at`, `idx_messages_issue_id`, `idx_messages_created_at`. Reads fail
soft (empty list) on a locked/corrupt file. Legacy databases (which used a mandatory issue-level
`created_by` column and no `note`) are migrated on load by rebuilding the `issues` table.

Issues support create, **edit** (title + note), and **delete** (cascades to messages). Export to
`.xlsx` via `IssueExcelExportService` (one or many selected issues, vertical Jira-style layout,
side-by-side blocks divided by a thick border, frozen top rows). Import a conversation from a JSON
file via `IssueConversationImportService` (`{ "messages": [...] }` or a bare array; merges into the
selected issue, skips exact duplicates, then re-sorts newest-first by date).

---

## Shared / supporting models

| Model | Purpose |
|-------|---------|
| `CatalogSettings`, `CatalogStore` | settings + in-memory catalog container |
| `EntryType`, `EntryFilter`, `SortMode`, `SearchSourceMode` | catalog enums |
| `EverythingResult` | a result row from Everything search |
| `AppTheme` | light/dark enum |
| `LanguageOption`, `LocalizedOption<T>` | localization-aware dropdown options |
| `PathPreviewFormatter` | shortens long paths for display |

> Schema changes here **must** be reflected in this file and `README.md` in the same change set.
