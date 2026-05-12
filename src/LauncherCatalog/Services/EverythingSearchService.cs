using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class EverythingSearchService
{
    private const int DefaultResultLimit = 30;
    private const uint EverythingRequestFullPathAndFileName = 0x00000004;
    private const string EmbeddedEverythingDllResourceName = "LauncherCatalog.Embedded.Everything64.dll";

    private readonly object _lock = new();
    private string? _sdkDllPath;
    private string? _appExecutablePath;
    private bool _pathsInitialized;
    private nint _sdkHandle;
    private bool _sdkLoadAttempted;
    private bool _sdkLoaded;

    public bool IsAvailable => TryLoadSdk() && !string.IsNullOrWhiteSpace(GetAppExecutablePath());
    public bool IsAppInstalled => !string.IsNullOrWhiteSpace(GetAppExecutablePath());

    public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        var appPath = GetAppExecutablePath();
        if (string.IsNullOrWhiteSpace(appPath))
        {
            return;
        }

        if (IsEverythingReady())
        {
            return;
        }

        StartBundledEverything(appPath, runAsAdministrator: false);
        if (await WaitUntilReadyAsync(cancellationToken))
        {
            return;
        }

        StartBundledEverything(appPath, runAsAdministrator: true);
        await WaitUntilReadyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EverythingResult>> SearchAsync(
        string? query,
        IReadOnlySet<string> savedPaths,
        CancellationToken cancellationToken = default)
    {
        var trimmedQuery = query?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuery) || !IsAvailable)
        {
            return [];
        }

        await EnsureStartedAsync(cancellationToken);
        if (!IsEverythingReady())
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

            var variantResults = SearchVariant(queryVariant, remainingSlots);
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

    private IReadOnlyList<string> SearchVariant(string query, int limit)
    {
        lock (_lock)
        {
            try
            {
                if (!TryLoadSdk())
                {
                    return [];
                }

                EverythingNative.Reset();
                EverythingNative.SetSearch(query);
                EverythingNative.SetRequestFlags(EverythingRequestFullPathAndFileName);
                EverythingNative.SetMax((uint)limit);
                EverythingNative.SetMatchPath(false);
                EverythingNative.SetRegex(false);
                EverythingNative.SetMatchCase(false);
                EverythingNative.SetMatchWholeWord(false);

                if (!EverythingNative.Query(wait: true))
                {
                    var error = EverythingNative.GetLastError();
                    AppLogger.WriteInfo($"Everything query failed. Error code: {error}");
                    return [];
                }

                var resultCount = (int)EverythingNative.GetNumResults();
                var paths = new List<string>(resultCount);
                var buffer = new StringBuilder(32768);

                for (uint index = 0; index < resultCount; index++)
                {
                    buffer.Clear();
                    var length = EverythingNative.GetResultFullPathName(index, buffer, (uint)buffer.Capacity);
                    if (length == 0)
                    {
                        continue;
                    }

                    var path = buffer.ToString();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path);
                    }
                }

                return paths;
            }
            catch
            {
                return [];
            }
        }
    }

    private bool IsEverythingReady()
    {
        lock (_lock)
        {
            try
            {
                if (!TryLoadSdk())
                {
                    return false;
                }

                return EverythingNative.IsDbLoaded();
            }
            catch
            {
                return false;
            }
        }
    }

    private async Task<bool> WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsEverythingReady())
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private static void StartBundledEverything(string appPath, bool runAsAdministrator)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = appPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Path.GetDirectoryName(appPath) ?? AppContext.BaseDirectory,
                Verb = runAsAdministrator ? "runas" : string.Empty
            };
            process.StartInfo.ArgumentList.Add("-startup");
            process.StartInfo.ArgumentList.Add("-minimized");
            process.StartInfo.ArgumentList.Add("-first-instance");
            if (runAsAdministrator)
            {
                process.StartInfo.ArgumentList.Add("-admin");
                process.StartInfo.ArgumentList.Add("-app-data");
            }
            process.Start();
        }
        catch
        {
            // Search calls will fall back to unavailable if the bundled app cannot start.
        }
    }

    private string? GetSdkDllPath()
    {
        EnsurePathsInitialized();
        return _sdkDllPath;
    }

    private string? GetAppExecutablePath()
    {
        EnsurePathsInitialized();
        return _appExecutablePath;
    }

    private void EnsurePathsInitialized()
    {
        lock (_lock)
        {
            if (_pathsInitialized)
            {
                return;
            }

            _sdkDllPath = FindKnownExecutable("Everything64.dll");
            _appExecutablePath = FindKnownExecutable("Everything.exe");
            _pathsInitialized = true;
        }
    }

    private static string? FindKnownExecutable(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "Everything", fileName),
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

        if (string.Equals(fileName, "Everything64.dll", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractEmbeddedSdkDll();
        }

        return null;
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

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().TrimEnd('\\', '/');
    }

    private bool TryLoadSdk()
    {
        lock (_lock)
        {
            if (_sdkLoaded)
            {
                return true;
            }

            if (_sdkLoadAttempted)
            {
                return false;
            }

            _sdkLoadAttempted = true;
            var sdkDllPath = GetSdkDllPath();
            if (string.IsNullOrWhiteSpace(sdkDllPath))
            {
                AppLogger.WriteInfo("Everything SDK DLL path could not be resolved.");
                return false;
            }

            try
            {
                _sdkLoaded = NativeLibrary.TryLoad(sdkDllPath, out _sdkHandle);
                if (!_sdkLoaded)
                {
                    AppLogger.WriteInfo($"Everything SDK DLL could not be loaded from: {sdkDllPath}");
                    return false;
                }

                AppLogger.WriteInfo($"Everything SDK DLL loaded from: {sdkDllPath}");
                return true;
            }
            catch (Exception exception)
            {
                AppLogger.WriteInfo($"Everything SDK DLL load failed: {exception.Message}");
                return false;
            }
        }
    }

    private static string? ExtractEmbeddedSdkDll()
    {
        try
        {
            using var resourceStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(EmbeddedEverythingDllResourceName);
            if (resourceStream is null)
            {
                return null;
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var targetDirectory = Path.Combine(appData, "LauncherCatalog", "Runtime", "Everything");
            Directory.CreateDirectory(targetDirectory);

            var targetPath = Path.Combine(targetDirectory, "Everything64.dll");
            using var fileStream = File.Create(targetPath);
            resourceStream.CopyTo(fileStream);
            fileStream.Flush();
            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    private static class EverythingNative
    {
        [DllImport("Everything64.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_SetSearchW")]
        internal static extern void SetSearch(string searchText);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMatchPath")]
        internal static extern void SetMatchPath([MarshalAs(UnmanagedType.Bool)] bool enabled);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMatchCase")]
        internal static extern void SetMatchCase([MarshalAs(UnmanagedType.Bool)] bool enabled);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMatchWholeWord")]
        internal static extern void SetMatchWholeWord([MarshalAs(UnmanagedType.Bool)] bool enabled);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetRegex")]
        internal static extern void SetRegex([MarshalAs(UnmanagedType.Bool)] bool enabled);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetMax")]
        internal static extern void SetMax(uint max);

        [DllImport("Everything64.dll", EntryPoint = "Everything_SetRequestFlags")]
        internal static extern void SetRequestFlags(uint flags);

        [DllImport("Everything64.dll", EntryPoint = "Everything_QueryW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Query([MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetNumResults")]
        internal static extern uint GetNumResults();

        [DllImport("Everything64.dll", CharSet = CharSet.Unicode, EntryPoint = "Everything_GetResultFullPathNameW")]
        internal static extern uint GetResultFullPathName(uint index, StringBuilder buffer, uint bufferSizeInWchars);

        [DllImport("Everything64.dll", EntryPoint = "Everything_IsDBLoaded")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsDbLoaded();

        [DllImport("Everything64.dll", EntryPoint = "Everything_GetLastError")]
        internal static extern uint GetLastError();

        [DllImport("Everything64.dll", EntryPoint = "Everything_Reset")]
        internal static extern void Reset();
    }
}
