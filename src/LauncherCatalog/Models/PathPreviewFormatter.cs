namespace LauncherCatalog.Models;

public static class PathPreviewFormatter
{
    private const int PreviewLength = 60;

    public static string Truncate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        return trimmed.Length <= PreviewLength
            ? trimmed
            : $"{trimmed[..PreviewLength]}...";
    }
}
