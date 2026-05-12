using System.Windows;

namespace LauncherCatalog.Models;

public sealed class NotePreviewLine
{
    public string Glyph { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public FontWeight FontWeight { get; init; } = FontWeights.Normal;
    public double FontSize { get; init; } = 13;
    public double Opacity { get; init; } = 1;
}
