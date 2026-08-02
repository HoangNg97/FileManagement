using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LauncherCatalog.Models;
using Microsoft.Data.Sqlite;

namespace LauncherCatalog.Services;

/// <summary>
/// SQLite-backed store for local issues and their message history (<c>issue.db</c> under the app
/// data folder). Mirrors the structure of the other repositories: a parent <c>issues</c> table and
/// a child <c>messages</c> table with a foreign key + cascade delete. All reads fail soft (returning
/// an empty list) so a locked/corrupt file never crashes the tab.
/// </summary>
public sealed class IssueRepository
{
    private readonly string _storageFolder;
    private readonly string _storagePath;
    private readonly string _connectionString;

    public IssueRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageFolder = Path.Combine(appData, "LauncherCatalog");
        _storagePath = Path.Combine(_storageFolder, "issue.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _storagePath
        }.ToString();
    }

    public string StoragePath => _storagePath;

    public async Task<IReadOnlyList<Issue>> LoadAsync()
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            await InitializeDatabaseAsync(connection);

            var issues = new List<Issue>();
            var lookup = new Dictionary<Guid, Issue>();

            await using (var issueCommand = connection.CreateCommand())
            {
                issueCommand.CommandText = """
                    SELECT id, title, note, created_at, updated_at
                    FROM issues
                    ORDER BY updated_at DESC;
                    """;

                await using var reader = await issueCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var issue = new Issue
                    {
                        Id = ParseGuid(ReadString(reader, 0)) ?? Guid.NewGuid(),
                        Title = ReadString(reader, 1) ?? string.Empty,
                        Note = ReadString(reader, 2) ?? string.Empty,
                        CreatedDate = ParseDate(ReadString(reader, 3)) ?? DateTime.Now,
                        UpdatedDate = ParseDate(ReadString(reader, 4)) ?? DateTime.Now
                    };

                    issues.Add(issue);
                    lookup[issue.Id] = issue;
                }
            }

            await using (var messageCommand = connection.CreateCommand())
            {
                messageCommand.CommandText = """
                    SELECT id, issue_id, created_by, created_at, content, modified_at, is_edited
                    FROM messages
                    ORDER BY created_at DESC;
                    """;

                await using var reader = await messageCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var issueId = ParseGuid(ReadString(reader, 1));
                    if (issueId is null || !lookup.TryGetValue(issueId.Value, out var issue))
                    {
                        continue;
                    }

                    issue.Messages.Add(new IssueMessage
                    {
                        Id = ParseGuid(ReadString(reader, 0)) ?? Guid.NewGuid(),
                        IssueId = issueId.Value,
                        CreatedBy = ReadString(reader, 2) ?? string.Empty,
                        CreatedDate = ParseDate(ReadString(reader, 3)) ?? DateTime.Now,
                        Content = ReadString(reader, 4) ?? string.Empty,
                        ModifiedDate = ParseDate(ReadString(reader, 5)),
                        IsEdited = ReadBool(reader, 6)
                    });
                }
            }

            return issues;
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

    public async Task CreateIssueAsync(Issue issue)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO issues (id, title, note, created_at, updated_at)
            VALUES ($id, $title, $note, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", issue.Id.ToString());
        command.Parameters.AddWithValue("$title", issue.Title.Trim());
        command.Parameters.AddWithValue("$note", issue.Note.Trim());
        command.Parameters.AddWithValue("$createdAt", ToIso(issue.CreatedDate));
        command.Parameters.AddWithValue("$updatedAt", ToIso(issue.UpdatedDate));
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateIssueAsync(Issue issue)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE issues
            SET title = $title,
                note = $note,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$title", issue.Title.Trim());
        command.Parameters.AddWithValue("$note", issue.Note.Trim());
        command.Parameters.AddWithValue("$updatedAt", ToIso(issue.UpdatedDate));
        command.Parameters.AddWithValue("$id", issue.Id.ToString());
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteIssueAsync(Guid issueId)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM issues WHERE id = $id;";
        command.Parameters.AddWithValue("$id", issueId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    public async Task SetIssueUpdatedAsync(Guid issueId, DateTime updatedAt)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE issues
            SET updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$updatedAt", ToIso(updatedAt));
        command.Parameters.AddWithValue("$id", issueId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddMessageAsync(IssueMessage message)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO messages (id, issue_id, created_by, created_at, content, modified_at, is_edited)
            VALUES ($id, $issueId, $createdBy, $createdAt, $content, $modifiedAt, $isEdited);
            """;
        AddMessageParameters(command, message);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateMessageAsync(IssueMessage message)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE messages
            SET created_by = $createdBy,
                created_at = $createdAt,
                content = $content,
                modified_at = $modifiedAt,
                is_edited = $isEdited
            WHERE id = $id;
            """;
        AddMessageParameters(command, message);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteMessageAsync(Guid messageId)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM messages WHERE id = $id;";
        command.Parameters.AddWithValue("$id", messageId.ToString());
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
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS issues (
                    id TEXT PRIMARY KEY NOT NULL,
                    title TEXT NOT NULL,
                    note TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS messages (
                    id TEXT PRIMARY KEY NOT NULL,
                    issue_id TEXT NOT NULL,
                    created_by TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    content TEXT NOT NULL,
                    modified_at TEXT NULL,
                    is_edited INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await MigrateIssuesTableAsync(connection);

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_issues_updated_at ON issues(updated_at);
                CREATE INDEX IF NOT EXISTS idx_messages_issue_id ON messages(issue_id);
                CREATE INDEX IF NOT EXISTS idx_messages_created_at ON messages(created_at);
                """;
            await indexCommand.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Brings an existing <c>issues</c> table up to the current schema. Older databases used a
    /// mandatory <c>created_by</c> column and had no <c>note</c> column; the issue-level author was
    /// dropped in favour of a free-text note. Because SQLite cannot drop a column in place, the table
    /// is rebuilt (data preserved) whenever the legacy column is still present.
    /// </summary>
    private static async Task MigrateIssuesTableAsync(SqliteConnection connection)
    {
        var hasCreatedBy = false;
        var hasNote = false;

        await using (var infoCommand = connection.CreateCommand())
        {
            infoCommand.CommandText = "PRAGMA table_info(issues);";
            await using var reader = await infoCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var columnName = reader.GetString(1);
                if (string.Equals(columnName, "created_by", StringComparison.OrdinalIgnoreCase))
                {
                    hasCreatedBy = true;
                }
                else if (string.Equals(columnName, "note", StringComparison.OrdinalIgnoreCase))
                {
                    hasNote = true;
                }
            }
        }

        if (!hasCreatedBy)
        {
            // Current schema (or already migrated). Just ensure the note column exists.
            if (!hasNote)
            {
                await using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE issues ADD COLUMN note TEXT NOT NULL DEFAULT '';";
                await alterCommand.ExecuteNonQueryAsync();
            }

            return;
        }

        // Legacy schema: rebuild the table without created_by, carrying over the note when present.
        var noteSource = hasNote ? "note" : "''";

        await using var rebuild = connection.CreateCommand();
        rebuild.CommandText = $"""
            PRAGMA foreign_keys=off;
            BEGIN TRANSACTION;
            CREATE TABLE issues_new (
                id TEXT PRIMARY KEY NOT NULL,
                title TEXT NOT NULL,
                note TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            INSERT INTO issues_new (id, title, note, created_at, updated_at)
                SELECT id, title, {noteSource}, created_at, updated_at FROM issues;
            DROP TABLE issues;
            ALTER TABLE issues_new RENAME TO issues;
            COMMIT;
            PRAGMA foreign_keys=on;
            """;
        await rebuild.ExecuteNonQueryAsync();
    }

    private static void AddMessageParameters(SqliteCommand command, IssueMessage message)
    {
        command.Parameters.AddWithValue("$id", message.Id.ToString());
        command.Parameters.AddWithValue("$issueId", message.IssueId.ToString());
        command.Parameters.AddWithValue("$createdBy", message.CreatedBy.Trim());
        command.Parameters.AddWithValue("$createdAt", ToIso(message.CreatedDate));
        command.Parameters.AddWithValue("$content", message.Content ?? string.Empty);
        command.Parameters.AddWithValue("$modifiedAt", ToDbValue(message.ModifiedDate?.ToString("O", CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$isEdited", message.IsEdited ? 1 : 0);
    }

    private static string ToIso(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
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
}
