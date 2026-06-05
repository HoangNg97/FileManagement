using System.Globalization;
using System.IO;
using System.Text.Json;
using LauncherCatalog.Models;
using Microsoft.Data.Sqlite;

namespace LauncherCatalog.Services;

public sealed class HistoryRepository
{
    private readonly string _storageFolder;
    private readonly string _storagePath;
    private readonly string _connectionString;

    public HistoryRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageFolder = Path.Combine(appData, "LauncherCatalog");
        _storagePath = Path.Combine(_storageFolder, "history.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _storagePath
        }.ToString();
    }

    public static string DefaultStoragePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "LauncherCatalog", "history.db");
        }
    }

    public string StoragePath => _storagePath;

    public async Task<IReadOnlyList<HistoryFileItem>> LoadFilesAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        var files = new List<HistoryFileItem>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,
                   name,
                   created_date,
                   tags_json,
                   is_favorite,
                   created_at,
                   updated_at
            FROM history_files
            ORDER BY is_favorite DESC, name COLLATE NOCASE ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            files.Add(new HistoryFileItem
            {
                Id = ParseGuid(ReadString(reader, 0)) ?? Guid.NewGuid(),
                Name = ReadString(reader, 1) ?? string.Empty,
                CreatedDate = ParseDate(ReadString(reader, 2)) ?? DateTime.Today,
                Tags = ReadTags(ReadString(reader, 3)),
                IsFavorite = ReadBool(reader, 4),
                CreatedAt = ParseDate(ReadString(reader, 5)) ?? DateTime.Now,
                UpdatedAt = ParseDate(ReadString(reader, 6)) ?? DateTime.Now
            });
        }

        return files;
    }

    public async Task CreateFileAsync(HistoryFileItem file)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO history_files (
                id,
                name,
                created_date,
                tags_json,
                is_favorite,
                created_at,
                updated_at
            )
            VALUES (
                $id,
                $name,
                $createdDate,
                $tagsJson,
                $isFavorite,
                $createdAt,
                $updatedAt
            );
            """;

        AddFileParameters(command, file);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateFileAsync(HistoryFileItem file)
    {
        file.UpdatedAt = DateTime.Now;

        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE history_files
            SET name = $name,
                created_date = $createdDate,
                tags_json = $tagsJson,
                is_favorite = $isFavorite,
                updated_at = $updatedAt
            WHERE id = $id;
            """;

        AddFileParameters(command, file);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteFileAsync(Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM history_files
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<HistoryEventItem>> LoadEventsAsync(Guid historyFileId)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        var events = new List<HistoryEventItem>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,
                   history_file_id,
                   event_date,
                   note,
                   created_at,
                   updated_at
            FROM history_events
            WHERE history_file_id = $historyFileId
            ORDER BY event_date ASC, created_at ASC;
            """;
        command.Parameters.AddWithValue("$historyFileId", historyFileId.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new HistoryEventItem
            {
                Id = ParseGuid(ReadString(reader, 0)) ?? Guid.NewGuid(),
                HistoryFileId = ParseGuid(ReadString(reader, 1)) ?? historyFileId,
                EventDate = ParseDate(ReadString(reader, 2)) ?? DateTime.Today,
                Note = ReadString(reader, 3) ?? string.Empty,
                CreatedAt = ParseDate(ReadString(reader, 4)) ?? DateTime.Now,
                UpdatedAt = ParseDate(ReadString(reader, 5)) ?? DateTime.Now
            });
        }

        return events;
    }

    public async Task CreateEventAsync(HistoryEventItem historyEvent)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO history_events (
                id,
                history_file_id,
                event_date,
                note,
                created_at,
                updated_at
            )
            VALUES (
                $id,
                $historyFileId,
                $eventDate,
                $note,
                $createdAt,
                $updatedAt
            );
            """;

        AddEventParameters(command, historyEvent);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateEventAsync(HistoryEventItem historyEvent)
    {
        historyEvent.UpdatedAt = DateTime.Now;

        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE history_events
            SET event_date = $eventDate,
                note = $note,
                updated_at = $updatedAt
            WHERE id = $id
              AND history_file_id = $historyFileId;
            """;

        AddEventParameters(command, historyEvent);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteEventAsync(Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM history_events
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
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
            CREATE TABLE IF NOT EXISTS history_files (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL,
                created_date TEXT NOT NULL,
                tags_json TEXT NOT NULL,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS history_events (
                id TEXT PRIMARY KEY NOT NULL,
                history_file_id TEXT NOT NULL,
                event_date TEXT NOT NULL,
                note TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(history_file_id) REFERENCES history_files(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_history_files_favorite ON history_files(is_favorite);
            CREATE INDEX IF NOT EXISTS idx_history_files_name ON history_files(name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS idx_history_events_file_date ON history_events(history_file_id, event_date);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static void AddFileParameters(SqliteCommand command, HistoryFileItem file)
    {
        command.Parameters.AddWithValue("$id", file.Id.ToString());
        command.Parameters.AddWithValue("$name", file.Name.Trim());
        command.Parameters.AddWithValue("$createdDate", file.CreatedDate.Date.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$tagsJson", JsonSerializer.Serialize(file.Tags));
        command.Parameters.AddWithValue("$isFavorite", file.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", file.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", file.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static void AddEventParameters(SqliteCommand command, HistoryEventItem historyEvent)
    {
        command.Parameters.AddWithValue("$id", historyEvent.Id.ToString());
        command.Parameters.AddWithValue("$historyFileId", historyEvent.HistoryFileId.ToString());
        command.Parameters.AddWithValue("$eventDate", historyEvent.EventDate.Date.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$note", historyEvent.Note.Trim());
        command.Parameters.AddWithValue("$createdAt", historyEvent.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", historyEvent.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string? ReadString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static bool ReadBool(SqliteDataReader reader, int ordinal)
    {
        return !reader.IsDBNull(ordinal) && reader.GetInt32(ordinal) != 0;
    }

    private static Guid? ParseGuid(string? rawValue)
    {
        return Guid.TryParse(rawValue, out var guid) ? guid : null;
    }

    private static DateTime? ParseDate(string? rawValue)
    {
        return DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;
    }

    private static List<string> ReadTags(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawValue) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
