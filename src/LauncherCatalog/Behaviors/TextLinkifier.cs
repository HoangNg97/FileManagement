using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using LauncherCatalog.Services;

namespace LauncherCatalog.Behaviors;

/// <summary>
/// Attached behaviour that renders a plain-text string inside a read-only <see cref="RichTextBox"/> and
/// turns any <c>http(s)</c> URLs it contains into clickable blue hyperlinks. Using a read-only
/// <see cref="RichTextBox"/> (rather than a <see cref="TextBlock"/>) keeps the text selectable/copyable
/// while still supporting single-click navigation. Clicking a link opens it in the OS default browser
/// (reusing an already-open browser window where the OS chooses to).
/// </summary>
public static class TextLinkifier
{
    // Matches http/https URLs. Trailing sentence punctuation is trimmed off in code so a URL that ends
    // a sentence (e.g. "see https://x.com.") does not swallow the period.
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s<>""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly DependencyProperty BoundTextProperty =
        DependencyProperty.RegisterAttached(
            "BoundText",
            typeof(string),
            typeof(TextLinkifier),
            new PropertyMetadata(string.Empty, OnBoundTextChanged));

    public static string GetBoundText(DependencyObject element) => (string)element.GetValue(BoundTextProperty);

    public static void SetBoundText(DependencyObject element, string value) => element.SetValue(BoundTextProperty, value);

    private static void OnBoundTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.RichTextBox richTextBox)
        {
            return;
        }

        richTextBox.IsReadOnly = true;
        richTextBox.IsDocumentEnabled = true;
        richTextBox.Document = BuildDocument(e.NewValue as string ?? string.Empty);
    }

    internal static FlowDocument BuildDocument(string text)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0)
        };

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0)
        };

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            AppendLine(paragraph, lines[i]);
            if (i < lines.Length - 1)
            {
                paragraph.Inlines.Add(new LineBreak());
            }
        }

        document.Blocks.Add(paragraph);
        return document;
    }

    private static void AppendLine(Paragraph paragraph, string line)
    {
        var lastIndex = 0;

        foreach (Match match in UrlPattern.Matches(line))
        {
            if (match.Index > lastIndex)
            {
                paragraph.Inlines.Add(new Run(line[lastIndex..match.Index]));
            }

            var (url, trailing) = SplitTrailingPunctuation(match.Value);
            paragraph.Inlines.Add(CreateHyperlink(url));

            if (!string.IsNullOrEmpty(trailing))
            {
                paragraph.Inlines.Add(new Run(trailing));
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < line.Length)
        {
            paragraph.Inlines.Add(new Run(line[lastIndex..]));
        }
    }

    private static Hyperlink CreateHyperlink(string url)
    {
        var hyperlink = new Hyperlink(new Run(url))
        {
            ToolTip = url
        };

        if (System.Windows.Application.Current?.TryFindResource("AccentBrush") is System.Windows.Media.Brush accent)
        {
            hyperlink.Foreground = accent;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            hyperlink.NavigateUri = uri;
        }

        hyperlink.RequestNavigate += OnRequestNavigate;
        return hyperlink;
    }

    private static void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        OpenUri(e.Uri);
    }

    internal static void OpenUri(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            AppLogger.WriteCrash(exception, $"Opening link '{uri.AbsoluteUri}'", isTerminating: false);
        }
    }


    /// <summary>Splits trailing sentence punctuation (e.g. <c>. , ) ] }</c>) off the end of a matched URL.</summary>
    private static (string Url, string Trailing) SplitTrailingPunctuation(string candidate)
    {
        var end = candidate.Length;
        while (end > 0 && ".,;:!?)]}>\"'".Contains(candidate[end - 1]))
        {
            end--;
        }

        return end == candidate.Length
            ? (candidate, string.Empty)
            : (candidate[..end], candidate[end..]);
    }
}
