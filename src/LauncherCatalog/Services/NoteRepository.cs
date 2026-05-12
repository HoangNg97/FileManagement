using System.Globalization;
using System.IO;
using System.Text.Json;
using LauncherCatalog.Models;
using Microsoft.Data.Sqlite;

namespace LauncherCatalog.Services;

public sealed class NoteRepository
{
    private readonly string _storageFolder;
    private readonly string _storagePath;
    private readonly string _connectionString;

    public NoteRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageFolder = Path.Combine(appData, "LauncherCatalog");
        _storagePath = Path.Combine(_storageFolder, "note.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _storagePath
        }.ToString();
    }

    public string StoragePath => _storagePath;

    public async Task<IReadOnlyList<NoteItem>> LoadAsync()
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            await InitializeDatabaseAsync(connection);

            var notes = new List<NoteItem>();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id,
                       title,
                       body,
                       tags_json,
                       is_favorite,
                       created_at,
                       updated_at,
                       last_viewed_at
                FROM notes
                ORDER BY is_favorite DESC,
                         COALESCE(last_viewed_at, updated_at) DESC,
                         updated_at DESC,
                         title COLLATE NOCASE ASC;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                notes.Add(new NoteItem
                {
                    Id = ParseGuid(ReadString(reader, 0)) ?? Guid.NewGuid(),
                    Title = ReadString(reader, 1) ?? string.Empty,
                    Body = ReadString(reader, 2) ?? string.Empty,
                    Tags = ReadTags(ReadString(reader, 3)),
                    IsFavorite = ReadBool(reader, 4),
                    CreatedAt = ParseDate(ReadString(reader, 5)) ?? DateTime.Now,
                    UpdatedAt = ParseDate(ReadString(reader, 6)) ?? DateTime.Now,
                    LastViewedAt = ParseDate(ReadString(reader, 7))
                });
            }

            return notes;
        }
        catch (SqliteException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public async Task CreateAsync(NoteItem note)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO notes (
                id,
                title,
                body,
                tags_json,
                is_favorite,
                created_at,
                updated_at,
                last_viewed_at
            )
            VALUES (
                $id,
                $title,
                $body,
                $tagsJson,
                $isFavorite,
                $createdAt,
                $updatedAt,
                $lastViewedAt
            );
            """;

        AddParameters(command, note);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(NoteItem note)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE notes
            SET title = $title,
                body = $body,
                tags_json = $tagsJson,
                is_favorite = $isFavorite,
                created_at = $createdAt,
                updated_at = $updatedAt,
                last_viewed_at = $lastViewedAt
            WHERE id = $id;
            """;

        AddParameters(command, note);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM notes
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
            CREATE TABLE IF NOT EXISTS notes (
                id TEXT PRIMARY KEY NOT NULL,
                title TEXT NOT NULL,
                body TEXT NOT NULL,
                tags_json TEXT NOT NULL,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_viewed_at TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_notes_is_favorite ON notes(is_favorite);
            CREATE INDEX IF NOT EXISTS idx_notes_updated_at ON notes(updated_at);
            CREATE INDEX IF NOT EXISTS idx_notes_last_viewed_at ON notes(last_viewed_at);
            CREATE INDEX IF NOT EXISTS idx_notes_title ON notes(title COLLATE NOCASE);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(SqliteCommand command, NoteItem note)
    {
        command.Parameters.AddWithValue("$id", note.Id.ToString());
        command.Parameters.AddWithValue("$title", note.Title.Trim());
        command.Parameters.AddWithValue("$body", note.Body ?? string.Empty);
        command.Parameters.AddWithValue("$tagsJson", JsonSerializer.Serialize(note.Tags));
        command.Parameters.AddWithValue("$isFavorite", note.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", note.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", note.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$lastViewedAt", ToDbValue(note.LastViewedAt?.ToString("O", CultureInfo.InvariantCulture)));
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
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
