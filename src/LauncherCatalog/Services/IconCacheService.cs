using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

public sealed class IconCacheService
{
    private readonly string _cacheFolder;

    public IconCacheService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheFolder = Path.Combine(appData, "LauncherCatalog", "IconCache");
    }

    public void RemoveIcon(CatalogEntry entry)
    {
        RemoveIcon(entry.Id);
    }

    public void RemoveIcon(Guid entryId)
    {
        try
        {
            if (!Directory.Exists(_cacheFolder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_cacheFolder, $"{entryId:N}-*.png"))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Icon cache cleanup is opportunistic; failed cleanup should never block catalog edits.
        }
    }

    public string? GetOrCreateIconPath(Guid entryId, EntryType type, string? filePath)
    {
        if (type != EntryType.App || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            RemoveIcon(entryId);
            return null;
        }

        try
        {
            Directory.CreateDirectory(_cacheFolder);

            var iconPath = Path.Combine(_cacheFolder, $"{entryId:N}-{GetCacheKey(filePath)}.png");
            if (File.Exists(iconPath))
            {
                return iconPath;
            }

            RemoveIcon(entryId);

            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            bitmap.Save(iconPath, ImageFormat.Png);
            return iconPath;
        }
        catch
        {
            return null;
        }
    }

    private static string GetCacheKey(string path)
    {
        var lastWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : string.Empty;
        var input = $"{path}|{lastWrite}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16].ToLowerInvariant();
    }
}
