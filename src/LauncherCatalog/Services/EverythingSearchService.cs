using System.Diagnostics;
using System.IO;
using System.Text;
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
        var trimmedQuery = query?.Trim();
        if (string.IsNullOrWhiteSpace(queryExecutablePath) || string.IsNullOrWhiteSpace(trimmedQuery))
        {
            return [];
        }

        var results = new List<EverythingResult>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var queryVariant in BuildQueryVariants(trimmedQuery))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingSlots = DefaultResultLimit - results.Count;
            if (remainingSlots <= 0)
            {
                break;
            }

            var variantResults = await SearchVariantAsync(
                queryExecutablePath,
                queryVariant,
                remainingSlots,
                cancellationToken);

            foreach (var path in variantResults)
            {
                if (!seenPaths.Add(path))
                {
                    continue;
                }

                results.Add(new EverythingResult
                {
                    FullPath = path,
                    IsFolder = Directory.Exists(path),
                    IsSaved = savedPaths.Contains(NormalizePath(path))
                });

                if (results.Count >= DefaultResultLimit)
                {
                    break;
                }
            }
        }

        return results;
    }

    private static async Task<IReadOnlyList<string>> SearchVariantAsync(
        string queryExecutablePath,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
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
        process.StartInfo.ArgumentList.Add(limit.ToString());
        process.StartInfo.ArgumentList.Add(query);

        var results = new List<string>();

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

                results.Add(line.Trim());
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

    private static IReadOnlyList<string> BuildQueryVariants(string query)
    {
        var variants = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddVariant(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var trimmed = candidate.Trim();
            if (seen.Add(trimmed))
            {
                variants.Add(trimmed);
            }
        }

        AddVariant(query);

        var collapsed = CollapseSearchText(query);
        AddVariant(collapsed);

        foreach (var value in new[] { query, collapsed })
        {
            var segments = SplitQuerySegments(value);
            if (segments.Count <= 1)
            {
                continue;
            }

            AddVariant(string.Join(" ", segments));
            AddVariant(string.Join("*", segments));
        }

        return variants;
    }

    private static List<string> SplitQuerySegments(string value)
    {
        var normalized = CollapseSearchText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var segments = new List<string>();
        var currentSegment = new StringBuilder();

        for (var index = 0; index < normalized.Length; index++)
        {
            var currentCharacter = normalized[index];

            if (currentSegment.Length > 0 && StartsNewSegment(currentSegment[^1], currentCharacter))
            {
                segments.Add(currentSegment.ToString());
                currentSegment.Clear();
            }

            currentSegment.Append(currentCharacter);
        }

        if (currentSegment.Length > 0)
        {
            segments.Add(currentSegment.ToString());
        }

        return segments;
    }

    private static bool StartsNewSegment(char previousCharacter, char currentCharacter)
    {
        return char.IsLetter(previousCharacter) != char.IsLetter(currentCharacter)
            || char.IsDigit(previousCharacter) != char.IsDigit(currentCharacter);
    }

    private static string CollapseSearchText(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray());
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
