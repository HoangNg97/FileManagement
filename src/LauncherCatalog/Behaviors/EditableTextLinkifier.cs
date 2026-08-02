using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace LauncherCatalog.Behaviors;

public static class EditableTextLinkifier
{
    private static readonly TimeSpan TypingLinkifyDelay = TimeSpan.FromSeconds(1);

    public static readonly DependencyProperty BoundTextProperty =
        DependencyProperty.RegisterAttached(
            "BoundText",
            typeof(string),
            typeof(EditableTextLinkifier),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundTextChanged));

    private static readonly DependencyProperty IsSynchronizingProperty =
        DependencyProperty.RegisterAttached(
            "IsSynchronizing",
            typeof(bool),
            typeof(EditableTextLinkifier),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsHooked",
            typeof(bool),
            typeof(EditableTextLinkifier),
            new PropertyMetadata(false));

    private static readonly DependencyProperty PendingTextProperty =
        DependencyProperty.RegisterAttached(
            "PendingText",
            typeof(string),
            typeof(EditableTextLinkifier),
            new PropertyMetadata(string.Empty));

    private static readonly DependencyProperty PendingCaretOffsetProperty =
        DependencyProperty.RegisterAttached(
            "PendingCaretOffset",
            typeof(int),
            typeof(EditableTextLinkifier),
            new PropertyMetadata(0));

    private static readonly DependencyProperty PendingVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "PendingVerticalOffset",
            typeof(double),
            typeof(EditableTextLinkifier),
            new PropertyMetadata(0d));

    private static readonly DependencyProperty ApplyTimerProperty =
        DependencyProperty.RegisterAttached(
            "ApplyTimer",
            typeof(DispatcherTimer),
            typeof(EditableTextLinkifier),
            new PropertyMetadata(null));

    public static string GetBoundText(DependencyObject element) => (string)element.GetValue(BoundTextProperty);

    public static void SetBoundText(DependencyObject element, string value) => element.SetValue(BoundTextProperty, value);

    private static bool GetIsSynchronizing(DependencyObject element) => (bool)element.GetValue(IsSynchronizingProperty);

    private static void SetIsSynchronizing(DependencyObject element, bool value) => element.SetValue(IsSynchronizingProperty, value);

    private static bool GetIsHooked(DependencyObject element) => (bool)element.GetValue(IsHookedProperty);

    private static void SetIsHooked(DependencyObject element, bool value) => element.SetValue(IsHookedProperty, value);

    private static string GetPendingText(DependencyObject element) => (string)element.GetValue(PendingTextProperty);

    private static void SetPendingText(DependencyObject element, string value) => element.SetValue(PendingTextProperty, value);

    private static int GetPendingCaretOffset(DependencyObject element) => (int)element.GetValue(PendingCaretOffsetProperty);

    private static void SetPendingCaretOffset(DependencyObject element, int value) => element.SetValue(PendingCaretOffsetProperty, value);

    private static double GetPendingVerticalOffset(DependencyObject element) => (double)element.GetValue(PendingVerticalOffsetProperty);

    private static void SetPendingVerticalOffset(DependencyObject element, double value) => element.SetValue(PendingVerticalOffsetProperty, value);

    private static DispatcherTimer? GetApplyTimer(DependencyObject element) =>
        (DispatcherTimer?)element.GetValue(ApplyTimerProperty);

    private static void SetApplyTimer(DependencyObject element, DispatcherTimer value) =>
        element.SetValue(ApplyTimerProperty, value);

    private static void OnBoundTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox richTextBox)
        {
            return;
        }

        EnsureHooked(richTextBox);

        if (GetIsSynchronizing(richTextBox))
        {
            return;
        }

        var text = e.NewValue as string ?? string.Empty;
        if (string.Equals(GetDocumentText(richTextBox.Document), text, StringComparison.Ordinal))
        {
            return;
        }

        QueueApplyDocument(
            richTextBox,
            text,
            Math.Min(GetCaretOffset(richTextBox), text.Length),
            verticalOffset: 0d,
            delay: TimeSpan.Zero);
    }

    private static void EnsureHooked(RichTextBox richTextBox)
    {
        if (GetIsHooked(richTextBox))
        {
            return;
        }

        richTextBox.IsDocumentEnabled = true;
        richTextBox.TextChanged += OnRichTextBoxTextChanged;
        richTextBox.PreviewMouseLeftButtonDown += OnRichTextBoxPreviewMouseLeftButtonDown;
        SetIsHooked(richTextBox, true);
    }

    private static void OnRichTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        var richTextBox = (RichTextBox)sender;
        if (GetIsSynchronizing(richTextBox))
        {
            return;
        }

        var text = GetDocumentText(richTextBox.Document);
        var caretOffset = Math.Min(GetCaretOffset(richTextBox), text.Length);

        QueueApplyDocument(
            richTextBox,
            text,
            caretOffset,
            richTextBox.VerticalOffset,
            TypingLinkifyDelay);

        if (string.Equals(GetBoundText(richTextBox), text, StringComparison.Ordinal))
        {
            return;
        }

        SetIsSynchronizing(richTextBox, true);
        try
        {
            richTextBox.SetCurrentValue(BoundTextProperty, text);
        }
        finally
        {
            SetIsSynchronizing(richTextBox, false);
        }
    }

    private static void QueueApplyDocument(
        RichTextBox richTextBox,
        string text,
        int caretOffset,
        double verticalOffset,
        TimeSpan delay)
    {
        SetPendingText(richTextBox, text);
        SetPendingCaretOffset(richTextBox, caretOffset);
        SetPendingVerticalOffset(richTextBox, verticalOffset);

        var timer = GetApplyTimer(richTextBox);
        if (timer is null)
        {
            timer = new DispatcherTimer(DispatcherPriority.Background, richTextBox.Dispatcher);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ApplyQueuedDocument(richTextBox);
            };
            SetApplyTimer(richTextBox, timer);
        }

        // Restarting the timer coalesces a burst of keystrokes into one hyperlink rebuild. This
        // avoids replacing the FlowDocument (and visibly flickering) after every character.
        timer.Stop();
        timer.Interval = delay;
        timer.Start();
    }

    private static void ApplyQueuedDocument(RichTextBox richTextBox)
    {
        if (GetIsSynchronizing(richTextBox))
        {
            return;
        }

        var text = GetPendingText(richTextBox);
        var caretOffset = Math.Min(GetPendingCaretOffset(richTextBox), text.Length);
        var verticalOffset = GetPendingVerticalOffset(richTextBox);
        ApplyDocument(richTextBox, text, caretOffset, verticalOffset);
    }

    private static void ApplyDocument(
        RichTextBox richTextBox,
        string text,
        int caretOffset,
        double verticalOffset)
    {
        SetIsSynchronizing(richTextBox, true);
        try
        {
            richTextBox.Document = TextLinkifier.BuildDocument(text);
            var caretPosition = GetTextPointerAtOffset(richTextBox.Document, caretOffset);
            richTextBox.CaretPosition = caretPosition;
            richTextBox.Selection.Select(caretPosition, caretPosition);
        }
        finally
        {
            SetIsSynchronizing(richTextBox, false);
        }

        // Replacing Document causes WPF to bring the restored caret into view, which shifts long
        // notes even when the caret was already visible. Wait until the new document has completed
        // layout, then restore the viewport captured immediately after the user's edit.
        richTextBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => richTextBox.ScrollToVerticalOffset(verticalOffset)));
    }

    private static string GetDocumentText(FlowDocument document)
    {
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        return text.EndsWith("\r\n", StringComparison.Ordinal)
            ? text[..^2]
            : text;
    }

    private static int GetCaretOffset(RichTextBox richTextBox)
    {
        var text = new TextRange(richTextBox.Document.ContentStart, richTextBox.CaretPosition).Text;
        if (richTextBox.CaretPosition.CompareTo(richTextBox.Document.ContentEnd) == 0 &&
            text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            text = text[..^2];
        }

        return text.Length;
    }

    private static TextPointer GetTextPointerAtOffset(FlowDocument document, int offset)
    {
        if (offset <= 0)
        {
            return document.ContentStart;
        }

        var navigator = document.ContentStart;
        var remaining = offset;

        while (navigator is not null)
        {
            switch (navigator.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    var runText = navigator.GetTextInRun(LogicalDirection.Forward);
                    if (remaining <= runText.Length)
                    {
                        return navigator.GetPositionAtOffset(remaining, LogicalDirection.Forward) ?? document.ContentEnd;
                    }

                    remaining -= runText.Length;
                    navigator = navigator.GetPositionAtOffset(runText.Length, LogicalDirection.Forward);
                    break;

                case TextPointerContext.ElementStart when navigator.GetAdjacentElement(LogicalDirection.Forward) is LineBreak:
                    if (remaining <= 2)
                    {
                        return navigator.GetNextContextPosition(LogicalDirection.Forward) ?? document.ContentEnd;
                    }

                    remaining -= 2;
                    navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
                    break;

                default:
                    navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
                    break;
            }
        }

        return document.ContentEnd;
    }

    private static void OnRichTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindHyperlink(e.OriginalSource as DependencyObject) is not { NavigateUri: not null } hyperlink)
        {
            return;
        }

        TextLinkifier.OpenUri(hyperlink.NavigateUri);
        e.Handled = true;
    }

    private static Hyperlink? FindHyperlink(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Hyperlink hyperlink)
            {
                return hyperlink;
            }

            source = source switch
            {
                FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
                FrameworkElement frameworkElement => frameworkElement.Parent,
                _ => null
            };
        }

        return null;
    }
}