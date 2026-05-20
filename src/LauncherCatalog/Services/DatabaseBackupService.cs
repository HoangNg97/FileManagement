using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace LauncherCatalog.Services;

public sealed class DatabaseBackupService : IDisposable
{
    private static readonly TimeSpan BackupInterval = TimeSpan.FromDays(2);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const int MaxBackupVersions = 10;

    private readonly DatabaseBackupTarget[] _targets;
    private readonly string _backupRootFolder;
    private readonly string _backupVersionsFolder;
    private readonly string _statePath;
    private readonly string _restoreGuidePath;
    private readonly DispatcherTimer _timer;
    private bool _isRunning;

    public DatabaseBackupService(string storageFolder, IEnumerable<DatabaseBackupTarget> targets)
    {
        _targets = targets
            .Where(target => target is not null)
            .Distinct(DatabaseBackupTargetComparer.Instance)
            .ToArray();
        _backupRootFolder = Path.Combine(storageFolder, "Backups");
        _backupVersionsFolder = Path.Combine(_backupRootFolder, "Versions");
        _statePath = Path.Combine(_backupRootFolder, "backup-state.json");
        _restoreGuidePath = Path.Combine(_backupRootFolder, "RESTORE.txt");
        _timer = new DispatcherTimer
        {
            Interval = CheckInterval
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        _ = RunIfDueAsync();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public IReadOnlyList<DatabaseBackupVersion> GetAvailableBackupVersions()
    {
        if (!Directory.Exists(_backupVersionsFolder))
        {
            return [];
        }

        return new DirectoryInfo(_backupVersionsFolder)
            .EnumerateDirectories()
            .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
            .Select(directory => new DatabaseBackupVersion(
                directory.Name,
                directory.FullName,
                ParseVersionCreatedAt(directory.Name)))
            .ToList();
    }

    public async Task<DatabaseRestoreResult> RestoreAsync(string versionName)
    {
        if (string.IsNullOrWhiteSpace(versionName))
        {
            return DatabaseRestoreResult.NoBackupFound;
        }

        var versionFolderPath = Path.Combine(_backupVersionsFolder, versionName);
        if (!Directory.Exists(versionFolderPath))
        {
            return DatabaseRestoreResult.NoBackupFound;
        }

        var restoreTargets = _targets
            .Select(target => new
            {
                Target = target,
                BackupPath = Path.Combine(versionFolderPath, target.BackupFileName)
            })
            .Where(item => File.Exists(item.BackupPath))
            .ToList();
        if (restoreTargets.Count == 0)
        {
            return DatabaseRestoreResult.NoBackupFound;
        }

        foreach (var item in restoreTargets)
        {
            var destinationFolder = Path.GetDirectoryName(item.Target.SourcePath);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            await RestoreDatabaseAsync(item.BackupPath, item.Target.SourcePath);
        }

        return DatabaseRestoreResult.Restored(versionName, versionFolderPath);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        await RunIfDueAsync();
    }

    private async Task RunIfDueAsync()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        try
        {
            await BackupIfDueAsync();
        }
        catch (Exception exception)
        {
            AppLogger.WriteInfo($"Database backup failed: {exception.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    private async Task BackupIfDueAsync()
    {
        var state = await LoadStateAsync();
        var nowUtc = DateTime.UtcNow;
        if (state.LastSuccessfulBackupUtc is { } lastBackupUtc
            && nowUtc - lastBackupUtc < BackupInterval)
        {
            return;
        }

        var backupTargets = _targets
            .Where(target => File.Exists(target.SourcePath))
            .ToList();
        if (backupTargets.Count == 0)
        {
            return;
        }

        EnsureBackupFolders();
        await SaveRestoreGuideAsync();

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var versionFolderPath = Path.Combine(_backupVersionsFolder, timestamp);
        Directory.CreateDirectory(versionFolderPath);

        try
        {
            foreach (var target in backupTargets)
            {
                var backupPath = Path.Combine(versionFolderPath, target.BackupFileName);
                await CreateBackupAsync(target.SourcePath, backupPath);
            }
        }
        catch
        {
            TryDeleteDirectory(versionFolderPath);
            throw;
        }

        state.LastSuccessfulBackupUtc = nowUtc;
        await SaveStateAsync(state);
        TrimOldBackupVersions();
        AppLogger.WriteInfo($"Database backup created in {versionFolderPath} at {DateTime.Now:O}.");
    }

    private static async Task CreateBackupAsync(string sourcePath, string backupPath)
    {
        await using var sourceConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await using var destinationConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        await sourceConnection.OpenAsync();
        await destinationConnection.OpenAsync();
        await Task.Run(() => sourceConnection.BackupDatabase(destinationConnection));
    }

    private static async Task RestoreDatabaseAsync(string backupPath, string destinationPath)
    {
        await using var sourceConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await using var destinationConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        await sourceConnection.OpenAsync();
        await destinationConnection.OpenAsync();
        await Task.Run(() => sourceConnection.BackupDatabase(destinationConnection));
    }

    private async Task<BackupState> LoadStateAsync()
    {
        if (!File.Exists(_statePath))
        {
            return new BackupState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_statePath);
            return JsonSerializer.Deserialize<BackupState>(json) ?? new BackupState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.WriteInfo($"Database backup state could not be read: {exception.Message}");
            return new BackupState();
        }
    }

    private async Task SaveStateAsync(BackupState state)
    {
        EnsureBackupFolders();

        var tempPath = $"{_statePath}.tmp";
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, _statePath, overwrite: true);
    }

    private static DateTime? ParseVersionCreatedAt(string versionName)
    {
        return DateTime.TryParseExact(
            versionName,
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private void EnsureBackupFolders()
    {
        Directory.CreateDirectory(_backupRootFolder);
        Directory.CreateDirectory(_backupVersionsFolder);
    }

    private async Task SaveRestoreGuideAsync()
    {
        var localizer = LocalizationService.Current;
        var lines = new List<string>
        {
            localizer.Get("Backup.RestoreGuide.Title"),
            string.Empty,
            localizer.Format("Backup.RestoreGuide.BackupRoot", _backupRootFolder),
            localizer.Format("Backup.RestoreGuide.VersionFolders", _backupVersionsFolder),
            localizer.Format("Backup.RestoreGuide.LiveDatabaseFolder", Path.GetDirectoryName(_targets.FirstOrDefault()?.SourcePath) ?? string.Empty),
            string.Empty,
            localizer.Get("Backup.RestoreGuide.StepsHeading"),
            localizer.Get("Backup.RestoreGuide.Step1"),
            localizer.Get("Backup.RestoreGuide.Step2"),
            localizer.Get("Backup.RestoreGuide.Step3"),
            localizer.Get("Backup.RestoreGuide.Step4"),
            localizer.Get("Backup.RestoreGuide.Step5"),
            string.Empty,
            localizer.Get("Backup.RestoreGuide.TrackedDatabases")
        };

        foreach (var target in _targets.OrderBy(target => target.BackupFileName, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- {target.BackupFileName} -> {target.SourcePath}");
        }

        await File.WriteAllLinesAsync(_restoreGuidePath, lines);
    }

    private void TrimOldBackupVersions()
    {
        if (!Directory.Exists(_backupVersionsFolder))
        {
            return;
        }

        var versionDirectories = new DirectoryInfo(_backupVersionsFolder)
            .EnumerateDirectories()
            .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var directory in versionDirectories.Skip(MaxBackupVersions))
        {
            TryDeleteDirectory(directory.FullName);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception)
        {
            AppLogger.WriteInfo($"Old backup cleanup skipped for {path}: {exception.Message}");
        }
    }

    private sealed class BackupState
    {
        public DateTime? LastSuccessfulBackupUtc { get; set; }
    }
}

public sealed record DatabaseBackupTarget(string SourcePath, string BackupFileName)
{
    public static DatabaseBackupTarget FromPath(string sourcePath, string? backupFileName = null)
    {
        return new DatabaseBackupTarget(
            sourcePath,
            string.IsNullOrWhiteSpace(backupFileName) ? Path.GetFileName(sourcePath) : backupFileName);
    }
}

public sealed record DatabaseRestoreResult(bool IsSuccess, string? VersionName, string? VersionPath)
{
    public static DatabaseRestoreResult NoBackupFound { get; } = new(false, null, null);

    public static DatabaseRestoreResult Restored(string versionName, string versionPath)
    {
        return new DatabaseRestoreResult(true, versionName, versionPath);
    }
}

public sealed record DatabaseBackupVersion(string VersionName, string VersionPath, DateTime? CreatedAtLocal)
{
    public string DisplayText => CreatedAtLocal?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? VersionName;
}

file sealed class DatabaseBackupTargetComparer : IEqualityComparer<DatabaseBackupTarget>
{
    public static DatabaseBackupTargetComparer Instance { get; } = new();

    public bool Equals(DatabaseBackupTarget? x, DatabaseBackupTarget? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return string.Equals(x.SourcePath, y.SourcePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.BackupFileName, y.BackupFileName, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(DatabaseBackupTarget obj)
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourcePath),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.BackupFileName));
    }
}
