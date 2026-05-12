using System.Globalization;
using System.IO;
using LauncherCatalog.Models;
using Microsoft.Data.Sqlite;

namespace LauncherCatalog.Services;

public sealed class TaskRepository
{
    private readonly string _storageFolder;
    private readonly string _storagePath;
    private readonly string _connectionString;

    public TaskRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageFolder = Path.Combine(appData, "LauncherCatalog");
        _storagePath = Path.Combine(_storageFolder, "task.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _storagePath
        }.ToString();
    }

    public string StoragePath => _storagePath;

    public async Task EnsureInitializedAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);
    }

    public async Task<IReadOnlyList<TaskItem>> LoadAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        var tasks = new List<TaskItem>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,
                   title,
                   description,
                   due_date,
                   status,
                   priority,
                   is_completed,
                   notifications_enabled,
                   last_reminder_key,
                   completed_at,
                   created_at,
                   updated_at,
                   sort_order
            FROM tasks
            ORDER BY is_completed ASC, due_date IS NULL ASC, due_date ASC, sort_order ASC, created_at ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tasks.Add(new TaskItem
            {
                Id = ParseGuid(ReadString(reader, 0)) ?? Guid.NewGuid(),
                Title = ReadString(reader, 1) ?? string.Empty,
                Description = ReadString(reader, 2),
                DueDate = ParseDate(ReadString(reader, 3)),
                Status = ParseStatus(ReadString(reader, 4)),
                Priority = ParsePriority(ReadString(reader, 5)),
                IsCompleted = ReadBool(reader, 6),
                NotificationsEnabled = ReadBool(reader, 7, defaultValue: true),
                LastReminderKey = ReadString(reader, 8),
                CompletedAt = ParseDate(ReadString(reader, 9)),
                CreatedAt = ParseDate(ReadString(reader, 10)) ?? DateTime.Now,
                UpdatedAt = ParseDate(ReadString(reader, 11)) ?? DateTime.Now,
                SortOrder = reader.IsDBNull(12) ? 0 : reader.GetInt32(12)
            });
        }

        return tasks;
    }

    public async Task CreateAsync(TaskItem task)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tasks (
                id,
                title,
                description,
                due_date,
                status,
                priority,
                is_completed,
                notifications_enabled,
                last_reminder_key,
                completed_at,
                created_at,
                updated_at,
                sort_order
            )
            VALUES (
                $id,
                $title,
                $description,
                $dueDate,
                $status,
                $priority,
                $isCompleted,
                $notificationsEnabled,
                $lastReminderKey,
                $completedAt,
                $createdAt,
                $updatedAt,
                $sortOrder
            );
            """;

        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$title", task.Title.Trim());
        command.Parameters.AddWithValue("$description", ToDbValue(task.Description));
        command.Parameters.AddWithValue("$dueDate", ToDbValue(task.DueDate?.ToString("O", CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$status", task.Status.ToString());
        command.Parameters.AddWithValue("$priority", task.Priority.ToString());
        command.Parameters.AddWithValue("$isCompleted", task.IsCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$notificationsEnabled", task.NotificationsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$lastReminderKey", ToDbValue(task.LastReminderKey));
        command.Parameters.AddWithValue("$completedAt", ToDbValue(task.CompletedAt?.ToString("O", CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", task.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sortOrder", task.SortOrder);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM tasks
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(TaskItem task)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        var now = DateTime.Now;
        var isCompleted = task.Status == Models.TaskStatus.Done || task.IsCompleted;
        var completedAt = isCompleted ? (task.CompletedAt ?? now) : (DateTime?)null;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks
            SET title = $title,
                description = $description,
                due_date = $dueDate,
                status = $status,
                priority = $priority,
                is_completed = $isCompleted,
                notifications_enabled = $notificationsEnabled,
                last_reminder_key = $lastReminderKey,
                completed_at = $completedAt,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$title", task.Title.Trim());
        command.Parameters.AddWithValue("$description", ToDbValue(task.Description));
        command.Parameters.AddWithValue("$dueDate", ToDbValue(task.DueDate?.ToString("O", CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$status", task.Status.ToString());
        command.Parameters.AddWithValue("$priority", task.Priority.ToString());
        command.Parameters.AddWithValue("$isCompleted", isCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$notificationsEnabled", task.NotificationsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$lastReminderKey", ToDbValue(task.LastReminderKey));
        command.Parameters.AddWithValue("$completedAt", ToDbValue(completedAt?.ToString("O", CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateNotificationStateAsync(Guid id, bool notificationsEnabled)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks
            SET notifications_enabled = $notificationsEnabled,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$notificationsEnabled", notificationsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateLastReminderKeyAsync(Guid id, string? reminderKey)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks
            SET last_reminder_key = $lastReminderKey
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$lastReminderKey", ToDbValue(reminderKey));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        Directory.CreateDirectory(_storageFolder);

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    private static async Task InitializeDatabaseAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS task_meta (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY NOT NULL,
                title TEXT NOT NULL,
                description TEXT NULL,
                due_date TEXT NULL,
                status TEXT NOT NULL,
                priority TEXT NOT NULL,
                is_completed INTEGER NOT NULL DEFAULT 0,
                notifications_enabled INTEGER NOT NULL DEFAULT 1,
                last_reminder_key TEXT NULL,
                completed_at TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks(status);
            CREATE INDEX IF NOT EXISTS idx_tasks_priority ON tasks(priority);
            CREATE INDEX IF NOT EXISTS idx_tasks_due_date ON tasks(due_date);
            CREATE INDEX IF NOT EXISTS idx_tasks_is_completed ON tasks(is_completed);
            CREATE INDEX IF NOT EXISTS idx_tasks_sort_order ON tasks(sort_order);
            CREATE INDEX IF NOT EXISTS idx_tasks_updated_at ON tasks(updated_at);
            """;
        await command.ExecuteNonQueryAsync();

        await EnsureColumnAsync(connection, "tasks", "notifications_enabled", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "tasks", "last_reminder_key", "TEXT NULL");

        await using var metaCommand = connection.CreateCommand();
        metaCommand.CommandText = """
            INSERT INTO task_meta (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        metaCommand.Parameters.AddWithValue("$key", "schema_version");
        metaCommand.Parameters.AddWithValue("$value", "2");
        await metaCommand.ExecuteNonQueryAsync();

        await using var touchedCommand = connection.CreateCommand();
        touchedCommand.CommandText = """
            INSERT INTO task_meta (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        touchedCommand.Parameters.AddWithValue("$key", "last_initialized_at");
        touchedCommand.Parameters.AddWithValue("$value", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        await touchedCommand.ExecuteNonQueryAsync();
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static string? ReadString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static bool ReadBool(SqliteDataReader reader, int ordinal, bool defaultValue = false)
    {
        return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt32(ordinal) != 0;
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(ReadString(reader, 1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.CloseAsync();

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync();
    }

    private static Guid? ParseGuid(string? value)
    {
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static DateTime? ParseDate(string? value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var date)
            ? date
            : null;
    }

    private static Models.TaskStatus ParseStatus(string? value)
    {
        return Enum.TryParse<Models.TaskStatus>(value, ignoreCase: true, out var status)
            ? status
            : Models.TaskStatus.Todo;
    }

    private static TaskPriority ParsePriority(string? value)
    {
        return Enum.TryParse<TaskPriority>(value, ignoreCase: true, out var priority)
            ? priority
            : TaskPriority.Normal;
    }
}
