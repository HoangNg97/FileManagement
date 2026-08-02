using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LauncherCatalog.Models;
using Microsoft.Data.Sqlite;

namespace LauncherCatalog.Services;

public sealed class EntryRepository
{
    private const string JsonMigrationKey = "json_migration_completed";

    private readonly string _storageFolder;
    private readonly string _storagePath;
    private readonly string _legacyStoragePath;
    private readonly string _legacyJsonPath;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public EntryRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageFolder = Path.Combine(appData, "LauncherCatalog");
        _storagePath = Path.Combine(_storageFolder, "catalog.db");
        _legacyStoragePath = Path.Combine(_storageFolder, "entries.db");
        _legacyJsonPath = Path.Combine(_storageFolder, "entries.json");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _storagePath
        }.ToString();
    }

    public string StoragePath => _storagePath;
    public CatalogSettings Settings { get; private set; } = new();

    public async Task<IReadOnlyList<CatalogEntry>> LoadAsync()
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            await InitializeDatabaseAsync(connection);
            await MigrateJsonIfNeededAsync(connection);

            Settings = await LoadSettingsAsync(connection);
            return await LoadEntriesAsync(connection);
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

    public async Task SaveAsync(IEnumerable<CatalogEntry> entries, CatalogSettings? settings = null)
    {
        if (settings is not null)
        {
            Settings = settings;
        }

        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);
        await SaveStoreAsync(connection, entries.ToList(), Settings);
    }

    public async Task<HashSet<Guid>> SearchEntryIdsAsync(string? query, EntryFilter filter, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        var trimmedQuery = query?.Trim();
        var ftsMatch = BuildFtsMatch(trimmedQuery);
        var filterSql = GetFilterSql(filter);
        var useFts = !string.IsNullOrWhiteSpace(ftsMatch);
        var useLike = !string.IsNullOrWhiteSpace(trimmedQuery);

        await using var command = connection.CreateCommand();
        command.CommandText = useFts
            ? $"""
                SELECT e.id
                FROM entries e
                WHERE 1 = 1
                      {filterSql}
                      AND (
                          e.id IN (
                              SELECT id
                              FROM entries_fts
                              WHERE entries_fts MATCH $ftsQuery
                          )
                          OR {GetLikeSearchPredicate()}
                      )
                ORDER BY e.is_favorite DESC, e.display_name COLLATE NOCASE ASC;
                """
            : $"""
                SELECT e.id
                FROM entries e
                WHERE 1 = 1
                      {filterSql}
                      {GetLikeSearchSql(useLike)}
                ORDER BY e.is_favorite DESC, e.display_name COLLATE NOCASE ASC;
                """;

        if (useFts)
        {
            command.Parameters.AddWithValue("$ftsQuery", ftsMatch!);
            command.Parameters.AddWithValue("$likeQuery", $"%{EscapeLike(trimmedQuery!)}%");
        }
        else if (useLike)
        {
            command.Parameters.AddWithValue("$likeQuery", $"%{EscapeLike(trimmedQuery!)}%");
        }

        var ids = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Guid.TryParse(reader.GetString(0), out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        Directory.CreateDirectory(_storageFolder);
        EnsureDatabaseFileRenamed();

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    private void EnsureDatabaseFileRenamed()
    {
        if (File.Exists(_storagePath) || !File.Exists(_legacyStoragePath))
        {
            return;
        }

        try
        {
            File.Move(_legacyStoragePath, _storagePath);
        }
        catch
        {
            try
            {
                File.Copy(_legacyStoragePath, _storagePath, overwrite: false);
            }
            catch
            {
                // If both move and copy fail, startup continues and SQLite will create a new catalog.db file.
            }
        }
    }

    private static async Task InitializeDatabaseAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS meta (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS entries (
                id TEXT PRIMARY KEY NOT NULL,
                type TEXT NOT NULL,
                display_name TEXT NOT NULL,
                exe_path TEXT NULL,
                exe_name TEXT NULL,
                folder_name TEXT NOT NULL,
                folder_path TEXT NOT NULL,
                tags_json TEXT NOT NULL,
                note TEXT NULL,
                date_added TEXT NOT NULL,
                last_opened TEXT NULL,
                is_missing INTEGER NOT NULL DEFAULT 0,
                is_favorite INTEGER NOT NULL DEFAULT 0
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS entries_fts USING fts5(
                id UNINDEXED,
                display_name,
                exe_name,
                folder_name,
                folder_path,
                exe_path,
                tags,
                note,
                tokenize='unicode61'
            );

            CREATE INDEX IF NOT EXISTS idx_entries_type ON entries(type);
            CREATE INDEX IF NOT EXISTS idx_entries_missing ON entries(is_missing);
            CREATE INDEX IF NOT EXISTS idx_entries_favorite ON entries(is_favorite);
            CREATE INDEX IF NOT EXISTS idx_entries_date_added ON entries(date_added);
            CREATE INDEX IF NOT EXISTS idx_entries_last_opened ON entries(last_opened);
            CREATE INDEX IF NOT EXISTS idx_entries_folder_path ON entries(folder_path COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS idx_entries_exe_path ON entries(exe_path COLLATE NOCASE);

            INSERT INTO entries_fts (
                id,
                display_name,
                exe_name,
                folder_name,
                folder_path,
                exe_path,
                tags,
                note
            )
            SELECT id,
                   display_name,
                   COALESCE(exe_name, ''),
                   folder_name,
                   folder_path,
                   COALESCE(exe_path, ''),
                   tags_json,
                   COALESCE(note, '')
            FROM entries
            WHERE id NOT IN (SELECT id FROM entries_fts);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task MigrateJsonIfNeededAsync(SqliteConnection connection)
    {
        if (!File.Exists(_legacyJsonPath)
            || await HasMetaValueAsync(connection, JsonMigrationKey)
            || await HasAnyEntriesAsync(connection))
        {
            return;
        }

        try
        {
            var store = await LoadJsonStoreAsync();
            await SaveStoreAsync(connection, store.Entries, store.Settings);
            await SetMetaValueAsync(connection, JsonMigrationKey, DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            CopyMigratedJsonBackup();
        }
        catch (JsonException)
        {
            BackupMalformedJsonFile();
        }
    }

    private async Task<CatalogStore> LoadJsonStoreAsync()
    {
        await using var stream = File.OpenRead(_legacyJsonPath);
        using var document = await JsonDocument.ParseAsync(stream);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return new CatalogStore
            {
                Settings = new CatalogSettings(),
                Entries = document.RootElement.Deserialize<List<CatalogEntry>>(_jsonOptions) ?? []
            };
        }

        return document.RootElement.Deserialize<CatalogStore>(_jsonOptions) ?? new CatalogStore();
    }

    public async Task<string?> GetSettingValueAsync(string key)
    {
        try
        {
            await using var connection = await OpenConnectionAsync();
            await InitializeDatabaseAsync(connection);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
            var result = await command.ExecuteScalarAsync();
            return result as string;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    public async Task SetSettingValueAsync(string key, string value)
    {
        await using var connection = await OpenConnectionAsync();
        await InitializeDatabaseAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<CatalogSettings> LoadSettingsAsync(SqliteConnection connection)
    {
        var settings = new CatalogSettings();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT key, value
            FROM settings
            WHERE key IN ('theme', 'quick_search_hotkey', 'language_code');
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = ReadString(reader, 0);
            var value = ReadString(reader, 1);

            if (key == "theme" && Enum.TryParse<AppTheme>(value, ignoreCase: true, out var theme))
            {
                settings.Theme = theme;
            }
            else if (key == "quick_search_hotkey" && !string.IsNullOrWhiteSpace(value))
            {
                settings.QuickSearchHotkey = value;
            }
            else if (key == "language_code" && !string.IsNullOrWhiteSpace(value))
            {
                settings.LanguageCode = value;
            }
        }

        return settings;
    }

    private async Task<List<CatalogEntry>> LoadEntriesAsync(SqliteConnection connection)
    {
        var entries = new List<CatalogEntry>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,
                   type,
                   display_name,
                   exe_path,
                   exe_name,
                   folder_name,
                   folder_path,
                   tags_json,
                   note,
                   date_added,
                   last_opened,
                   is_missing,
                   is_favorite
            FROM entries
            ORDER BY is_favorite DESC, display_name COLLATE NOCASE ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new CatalogEntry
            {
                Id = ParseGuid(ReadString(reader, 0)) ?? Guid.NewGuid(),
                Type = ParseEnum(ReadString(reader, 1), EntryType.App),
                DisplayName = ReadString(reader, 2) ?? string.Empty,
                ExePath = ReadString(reader, 3),
                ExeName = ReadString(reader, 4),
                FolderName = ReadString(reader, 5) ?? string.Empty,
                FolderPath = ReadString(reader, 6) ?? string.Empty,
                Tags = ParseTags(ReadString(reader, 7)),
                Note = ReadString(reader, 8),
                DateAdded = ParseDate(ReadString(reader, 9)) ?? DateTime.Now,
                LastOpened = ParseDate(ReadString(reader, 10)),
                IsMissing = ReadBool(reader, 11),
                IsFavorite = ReadBool(reader, 12)
            });
        }

        return entries;
    }

    private async Task SaveStoreAsync(SqliteConnection connection, IReadOnlyCollection<CatalogEntry> entries, CatalogSettings settings)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await SaveSettingsAsync(connection, transaction, settings);
        await SaveEntriesAsync(connection, transaction, entries);

        await transaction.CommitAsync();
        Settings = settings;
    }

    private static async Task SaveSettingsAsync(SqliteConnection connection, SqliteTransaction transaction, CatalogSettings settings)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO settings (key, value)
            VALUES ('theme', $theme)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO settings (key, value)
            VALUES ('quick_search_hotkey', $quickSearchHotkey)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO settings (key, value)
            VALUES ('language_code', $languageCode)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$theme", settings.Theme.ToString());
        command.Parameters.AddWithValue("$quickSearchHotkey", settings.QuickSearchHotkey);
        command.Parameters.AddWithValue("$languageCode", settings.LanguageCode);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SaveEntriesAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyCollection<CatalogEntry> entries)
    {
        await using (var prepareCommand = connection.CreateCommand())
        {
            prepareCommand.Transaction = transaction;
            prepareCommand.CommandText = """
                CREATE TEMP TABLE IF NOT EXISTS current_entry_ids (id TEXT PRIMARY KEY NOT NULL);
                DELETE FROM current_entry_ids;
                """;
            await prepareCommand.ExecuteNonQueryAsync();
        }

        foreach (var entry in entries)
        {
            await using (var idCommand = connection.CreateCommand())
            {
                idCommand.Transaction = transaction;
                idCommand.CommandText = "INSERT OR IGNORE INTO current_entry_ids (id) VALUES ($id);";
                idCommand.Parameters.AddWithValue("$id", entry.Id.ToString());
                await idCommand.ExecuteNonQueryAsync();
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO entries (
                    id,
                    type,
                    display_name,
                    exe_path,
                    exe_name,
                    folder_name,
                    folder_path,
                    tags_json,
                    note,
                    date_added,
                    last_opened,
                    is_missing,
                    is_favorite
                )
                VALUES (
                    $id,
                    $type,
                    $displayName,
                    $exePath,
                    $exeName,
                    $folderName,
                    $folderPath,
                    $tagsJson,
                    $note,
                    $dateAdded,
                    $lastOpened,
                    $isMissing,
                    $isFavorite
                )
                ON CONFLICT(id) DO UPDATE SET
                    type = excluded.type,
                    display_name = excluded.display_name,
                    exe_path = excluded.exe_path,
                    exe_name = excluded.exe_name,
                    folder_name = excluded.folder_name,
                    folder_path = excluded.folder_path,
                    tags_json = excluded.tags_json,
                    note = excluded.note,
                    date_added = excluded.date_added,
                    last_opened = excluded.last_opened,
                    is_missing = excluded.is_missing,
                    is_favorite = excluded.is_favorite;
                """;
            AddEntryParameters(command, entry);
            await command.ExecuteNonQueryAsync();

            await using (var deleteSearchCommand = connection.CreateCommand())
            {
                deleteSearchCommand.Transaction = transaction;
                deleteSearchCommand.CommandText = "DELETE FROM entries_fts WHERE id = $id;";
                deleteSearchCommand.Parameters.AddWithValue("$id", entry.Id.ToString());
                await deleteSearchCommand.ExecuteNonQueryAsync();
            }

            await using (var searchCommand = connection.CreateCommand())
            {
                searchCommand.Transaction = transaction;
                searchCommand.CommandText = """
                    INSERT INTO entries_fts (
                        id,
                        display_name,
                        exe_name,
                        folder_name,
                        folder_path,
                        exe_path,
                        tags,
                        note
                    )
                    VALUES (
                        $id,
                        $displayName,
                        $exeName,
                        $folderName,
                        $folderPath,
                        $exePath,
                        $tags,
                        $note
                    );
                    """;
                AddSearchParameters(searchCommand, entry);
                await searchCommand.ExecuteNonQueryAsync();
            }
        }

        await using (var cleanupCommand = connection.CreateCommand())
        {
            cleanupCommand.Transaction = transaction;
            cleanupCommand.CommandText = "DELETE FROM entries WHERE id NOT IN (SELECT id FROM current_entry_ids);";
            await cleanupCommand.ExecuteNonQueryAsync();
        }

        await using (var searchCleanupCommand = connection.CreateCommand())
        {
            searchCleanupCommand.Transaction = transaction;
            searchCleanupCommand.CommandText = "DELETE FROM entries_fts WHERE id NOT IN (SELECT id FROM current_entry_ids);";
            await searchCleanupCommand.ExecuteNonQueryAsync();
        }
    }

    private void AddEntryParameters(SqliteCommand command, CatalogEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$type", entry.Type.ToString());
        command.Parameters.AddWithValue("$displayName", entry.DisplayName);
        command.Parameters.AddWithValue("$exePath", ToDbValue(entry.ExePath));
        command.Parameters.AddWithValue("$exeName", ToDbValue(entry.ExeName));
        command.Parameters.AddWithValue("$folderName", entry.FolderName);
        command.Parameters.AddWithValue("$folderPath", entry.FolderPath);
        command.Parameters.AddWithValue("$tagsJson", JsonSerializer.Serialize(entry.Tags, _jsonOptions));
        command.Parameters.AddWithValue("$note", ToDbValue(entry.Note));
        command.Parameters.AddWithValue("$dateAdded", entry.DateAdded.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$lastOpened", ToDbValue(entry.LastOpened?.ToString("O", CultureInfo.InvariantCulture)));
        command.Parameters.AddWithValue("$isMissing", entry.IsMissing ? 1 : 0);
        command.Parameters.AddWithValue("$isFavorite", entry.IsFavorite ? 1 : 0);
    }

    private static void AddSearchParameters(SqliteCommand command, CatalogEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$displayName", entry.DisplayName);
        command.Parameters.AddWithValue("$exeName", entry.ExeName ?? string.Empty);
        command.Parameters.AddWithValue("$folderName", entry.FolderName);
        command.Parameters.AddWithValue("$folderPath", entry.FolderPath);
        command.Parameters.AddWithValue("$exePath", entry.ExePath ?? string.Empty);
        command.Parameters.AddWithValue("$tags", string.Join(' ', entry.Tags));
        command.Parameters.AddWithValue("$note", entry.Note ?? string.Empty);
    }

    private static async Task<bool> HasAnyEntriesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM entries LIMIT 1);";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> HasMetaValueAsync(SqliteConnection connection, string key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM meta WHERE key = $key);";
        command.Parameters.AddWithValue("$key", key);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task SetMetaValueAsync(SqliteConnection connection, string key, string value)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO meta (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static string GetFilterSql(EntryFilter filter)
    {
        return filter switch
        {
            EntryFilter.Apps => "AND e.type = 'App'",
            EntryFilter.Folders => "AND e.type = 'Folder'",
            EntryFilter.Missing => "AND e.is_missing = 1",
            _ => string.Empty
        };
    }

    private static string GetLikeSearchSql(bool useLike)
    {
        return useLike
            ? $"AND ({GetLikeSearchPredicate()})"
            : string.Empty;
    }

    private static string GetLikeSearchPredicate()
    {
        return """
            e.display_name LIKE $likeQuery ESCAPE '\'
            OR e.exe_name LIKE $likeQuery ESCAPE '\'
            OR e.folder_name LIKE $likeQuery ESCAPE '\'
            OR e.folder_path LIKE $likeQuery ESCAPE '\'
            OR e.exe_path LIKE $likeQuery ESCAPE '\'
            OR e.tags_json LIKE $likeQuery ESCAPE '\'
            OR e.note LIKE $likeQuery ESCAPE '\'
            """;
    }

    private static string? BuildFtsMatch(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var terms = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanFtsTerm)
            .Where(term => term.Length > 0)
            .Select(term => $"{term}*")
            .ToArray();

        return terms.Length == 0 ? null : string.Join(" AND ", terms);
    }

    private static string CleanFtsTerm(string term)
    {
        return new string(term.Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray());
    }

    private static string EscapeLike(string query)
    {
        return query
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    private void CopyMigratedJsonBackup()
    {
        try
        {
            var backupPath = Path.Combine(_storageFolder, $"entries.migrated-{DateTime.Now:yyyyMMddHHmmss}.json");
            File.Copy(_legacyJsonPath, backupPath, overwrite: false);
        }
        catch
        {
            // The SQLite import has already succeeded; a failed compatibility backup should not block startup.
        }
    }

    private void BackupMalformedJsonFile()
    {
        try
        {
            var backupPath = Path.Combine(_storageFolder, $"entries.invalid-{DateTime.Now:yyyyMMddHHmmss}.json");
            File.Move(_legacyJsonPath, backupPath, overwrite: true);
        }
        catch
        {
            // If recovery backup fails, continue with an empty catalog rather than crashing startup.
        }
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

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) ? result : fallback;
    }

    private static List<string> ParseTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(tagsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
