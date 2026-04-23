using System.Diagnostics;
using System.IO;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class EverythingSearchService
{
    private const int DefaultResultLimit = 30;

    private readonly object _lock = new();
    private string? _queryExecutablePath;
    private string? _appExecutablePath;
    private bool _knownLocationsInitialized;
    private bool _pathInitialized;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(GetQueryExecutablePath(includePath: false));
    public bool IsAppInstalled => !string.IsNullOrWhiteSpace(GetAppExecutablePath(includePath: false));

    public async Task<IReadOnlyList<EverythingResult>> SearchAsync(
        string? query,
        IReadOnlySet<string> savedPaths,
        CancellationToken cancellationToken = default)
    {
        var queryExecutablePath = GetQueryExecutablePath(includePath: true);
        if (string.IsNullOrWhiteSpace(queryExecutablePath) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = queryExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("-n");
        process.StartInfo.ArgumentList.Add(DefaultResultLimit.ToString());
        process.StartInfo.ArgumentList.Add(query.Trim());

        var results = new List<EverythingResult>();

        try
        {
            process.Start();

            await foreach (var line in ReadLinesAsync(process.StandardOutput, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var path = line.Trim();
                results.Add(new EverythingResult
                {
                    FullPath = path,
                    IsFolder = Directory.Exists(path),
                    IsSaved = savedPaths.Contains(NormalizePath(path))
                });
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch
        {
            return [];
        }

        return results;
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is not null)
            {
                yield return line;
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may have exited between cancellation and kill.
        }
    }

    private string? GetQueryExecutablePath(bool includePath)
    {
        InitializeExecutablePaths(includePath);
        return _queryExecutablePath;
    }

    private string? GetAppExecutablePath(bool includePath)
    {
        InitializeExecutablePaths(includePath);
        return _appExecutablePath;
    }

    private void InitializeExecutablePaths(bool includePath)
    {
        lock (_lock)
        {
            if (!_knownLocationsInitialized)
            {
                _queryExecutablePath = FindKnownExecutable("es.exe");
                _appExecutablePath = FindKnownExecutable("Everything.exe");
                _knownLocationsInitialized = true;
            }

            if (!includePath || _pathInitialized)
            {
                return;
            }

            _queryExecutablePath ??= FindOnPath("es.exe");
            _appExecutablePath ??= FindOnPath("Everything.exe");
            _pathInitialized = true;
        }
    }

    private static string? FindKnownExecutable(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", fileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", fileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var folder in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimEnd('\\', '/');
    }
}
