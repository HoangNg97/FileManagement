using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LauncherCatalog.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace LauncherCatalog.Views;

public sealed class HistoryRouteMap : FrameworkElement
{
    private const double DefaultWidth = 860;
    private const double TopPadding = 126;
    private const double BottomPadding = 110;
    private const double MinimumStepY = 132;
    private const double HitRadius = 22;
    private const double CardMinWidth = 230;
    private const double CardMaxWidth = 430;
    private const double CardPaddingX = 10;
    private const double DotToCardGap = 28;

    private readonly Dictionary<Guid, Point> _eventPoints = [];
    private readonly Dictionary<Guid, Rect> _eventHitAreas = [];

    public static readonly DependencyProperty EventsProperty =
        DependencyProperty.Register(
            nameof(Events),
            typeof(IEnumerable),
            typeof(HistoryRouteMap),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                OnEventsChanged));

    public static readonly DependencyProperty SelectedEventProperty =
        DependencyProperty.Register(
            nameof(SelectedEvent),
            typeof(HistoryEventItem),
            typeof(HistoryRouteMap),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightedEventIdProperty =
        DependencyProperty.Register(
            nameof(HighlightedEventId),
            typeof(Guid?),
            typeof(HistoryRouteMap),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EventSelectedCommandProperty =
        DependencyProperty.Register(
            nameof(EventSelectedCommand),
            typeof(ICommand),
            typeof(HistoryRouteMap),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ClearHighlightCommandProperty =
        DependencyProperty.Register(
            nameof(ClearHighlightCommand),
            typeof(ICommand),
            typeof(HistoryRouteMap),
            new PropertyMetadata(null));

    public HistoryRouteMap()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
    }

    public event EventHandler<HistoryEventItem>? EventClicked;

    public IEnumerable? Events
    {
        get => (IEnumerable?)GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    private static void OnEventsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not HistoryRouteMap routeMap)
        {
            return;
        }

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= routeMap.Events_CollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += routeMap.Events_CollectionChanged;
        }
    }

    private void Events_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    public HistoryEventItem? SelectedEvent
    {
        get => (HistoryEventItem?)GetValue(SelectedEventProperty);
        set => SetValue(SelectedEventProperty, value);
    }

    public Guid? HighlightedEventId
    {
        get => (Guid?)GetValue(HighlightedEventIdProperty);
        set => SetValue(HighlightedEventIdProperty, value);
    }

    public ICommand? EventSelectedCommand
    {
        get => (ICommand?)GetValue(EventSelectedCommandProperty);
        set => SetValue(EventSelectedCommandProperty, value);
    }

    public ICommand? ClearHighlightCommand
    {
        get => (ICommand?)GetValue(ClearHighlightCommandProperty);
        set => SetValue(ClearHighlightCommandProperty, value);
    }

    public void ScrollEventIntoView(Guid eventId)
    {
        var events = GetEvents();
        RebuildEventPoints(events, Math.Max(ActualWidth, 420));
        if (!_eventPoints.TryGetValue(eventId, out var point))
        {
            return;
        }

        BringIntoView(new Rect(
            Math.Max(0, point.X - 140),
            Math.Max(0, point.Y - 110),
            280,
            220));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? DefaultWidth
            : Math.Max(availableSize.Width, 420);

        return new Size(width, CalculateHeight(GetEvents()));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var events = GetEvents();
        _eventPoints.Clear();
        _eventHitAreas.Clear();
        if (events.Count == 0)
        {
            return;
        }

        var width = Math.Max(ActualWidth, 420);
        var pathBrush = GetBrush("MutedTextBrush", Color.FromRgb(88, 94, 105));
        var accentBrush = GetBrush("AccentBrush", Color.FromRgb(37, 99, 235));
        var surfaceBrush = GetBrush("SurfaceBrush", Colors.White);
        var strongTextBrush = GetBrush("StrongTextBrush", Color.FromRgb(23, 32, 51));
        var mutedTextBrush = GetBrush("MutedTextBrush", Color.FromRgb(101, 117, 139));
        var successBrush = GetBrush("SuccessBrush", Color.FromRgb(8, 116, 67));
        var borderBrush = GetBrush("BorderBrush", Color.FromRgb(221, 229, 240));

        RebuildEventPoints(events, width);
        DrawRoute(drawingContext, events, pathBrush);

        foreach (var historyEvent in events)
        {
            if (!_eventPoints.TryGetValue(historyEvent.Id, out var point))
            {
                continue;
            }

            var isSelected = SelectedEvent?.Id == historyEvent.Id;
            var isHighlighted = HighlightedEventId == historyEvent.Id;
            DrawMilestone(
                drawingContext,
                historyEvent,
                point,
                width,
                isSelected,
                isHighlighted,
                accentBrush,
                surfaceBrush,
                strongTextBrush,
                mutedTextBrush,
                successBrush,
                borderBrush);
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var position = e.GetPosition(this);
        var events = GetEvents();
        var calloutHit = _eventHitAreas
            .FirstOrDefault(pair => pair.Value.Contains(position));
        if (calloutHit.Key != Guid.Empty
            && events.FirstOrDefault(item => item.Id == calloutHit.Key) is { } calloutEvent)
        {
            OpenEvent(calloutEvent);
            e.Handled = true;
            return;
        }

        var hitEvent = _eventPoints
            .Select(pair => new
            {
                Event = events.FirstOrDefault(item => item.Id == pair.Key),
                Distance = (pair.Value - position).Length
            })
            .Where(item => item.Event is not null)
            .OrderBy(item => item.Distance)
            .FirstOrDefault();

        if (hitEvent is not null && hitEvent.Distance <= HitRadius)
        {
            if (hitEvent.Event is not null)
            {
                OpenEvent(hitEvent.Event);
            }

            e.Handled = true;
            return;
        }

        if (ClearHighlightCommand?.CanExecute(null) == true)
        {
            ClearHighlightCommand.Execute(null);
        }
    }

    private void OpenEvent(HistoryEventItem historyEvent)
    {
        if (EventSelectedCommand?.CanExecute(historyEvent) == true)
        {
            EventSelectedCommand.Execute(historyEvent);
        }

        EventClicked?.Invoke(this, historyEvent);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (ClearHighlightCommand?.CanExecute(null) == true)
        {
            ClearHighlightCommand.Execute(null);
        }
    }

    private void DrawRoute(DrawingContext drawingContext, IReadOnlyList<HistoryEventItem> events, Brush pathBrush)
    {
        if (events.Count == 1)
        {
            var point = _eventPoints[events[0].Id];
            drawingContext.DrawEllipse(null, new Pen(pathBrush, 9), point, 13, 13);
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var firstPoint = _eventPoints[events[0].Id];
            context.BeginFigure(firstPoint, isFilled: false, isClosed: false);

            for (var index = 1; index < events.Count; index++)
            {
                var previous = _eventPoints[events[index - 1].Id];
                var current = _eventPoints[events[index].Id];
                var midY = previous.Y + (current.Y - previous.Y) / 2;
                context.BezierTo(
                    new Point(previous.X, midY),
                    new Point(current.X, midY),
                    current,
                    isStroked: true,
                    isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        var pen = new Pen(pathBrush, 9)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private void DrawMilestone(
        DrawingContext drawingContext,
        HistoryEventItem historyEvent,
        Point point,
        double width,
        bool isSelected,
        bool isHighlighted,
        Brush accentBrush,
        Brush surfaceBrush,
        Brush strongTextBrush,
        Brush mutedTextBrush,
        Brush successBrush,
        Brush borderBrush)
    {
        var ringBrush = isHighlighted ? successBrush : accentBrush;
        var outerRadius = isSelected ? 16 : 14;
        drawingContext.DrawEllipse(surfaceBrush, new Pen(ringBrush, 4), point, outerRadius, outerRadius);
        drawingContext.DrawEllipse(ringBrush, null, point, 5, 5);

        var textBrush = isHighlighted ? successBrush : strongTextBrush;
        var dateText = CreateText(historyEvent.EventDateText, textBrush, 13, FontWeights.SemiBold, 160);
        var noteLines = WrapText(historyEvent.Note, 60);
        var cardMetrics = MeasureCard(noteLines, dateText, point, width);
        var noteTexts = noteLines
            .Select(line => CreateText(line, isHighlighted ? successBrush : mutedTextBrush, 12, FontWeights.Normal, cardMetrics.TextWidth))
            .ToList();

        var textX = cardMetrics.CardX + CardPaddingX;
        var textY = point.Y - 28;

        var noteHeight = noteTexts.Sum(text => text.Height + 2);
        var backgroundRect = new Rect(cardMetrics.CardX, textY - 8, cardMetrics.CardWidth, Math.Max(62, noteHeight + 34));
        _eventHitAreas[historyEvent.Id] = backgroundRect;
        var calloutBrush = surfaceBrush.CloneCurrentValue();
        calloutBrush.Opacity = 0.92;
        if (calloutBrush.CanFreeze)
        {
            calloutBrush.Freeze();
        }

        drawingContext.DrawRoundedRectangle(
            calloutBrush,
            new Pen(borderBrush, isSelected ? 1.5 : 1),
            backgroundRect,
            8,
            8);
        drawingContext.DrawText(dateText, new Point(textX, textY));
        var lineY = textY + 22;
        foreach (var noteText in noteTexts)
        {
            drawingContext.DrawText(noteText, new Point(textX, lineY));
            lineY += noteText.Height + 2;
        }
    }

    private FormattedText CreateText(string text, Brush brush, double fontSize, FontWeight fontWeight, double maxWidth)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new FormattedText(
            string.IsNullOrWhiteSpace(text) ? " " : text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, fontWeight, FontStretches.Normal),
            fontSize,
            brush,
            dpi.PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            Trimming = TextTrimming.None
        };
    }

    private CardMetrics MeasureCard(IReadOnlyList<string> noteLines, FormattedText dateText, Point point, double width)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var widestNoteLine = noteLines
            .Select(line => new FormattedText(
                string.IsNullOrWhiteSpace(line) ? " " : line,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                12,
                System.Windows.Media.Brushes.Black,
                dpi.PixelsPerDip).WidthIncludingTrailingWhitespace)
            .DefaultIfEmpty(0)
            .Max();

        var desiredWidth = Math.Clamp(Math.Max(dateText.WidthIncludingTrailingWhitespace, widestNoteLine) + CardPaddingX * 2, CardMinWidth, CardMaxWidth);
        var margin = 16d;
        var rightCapacity = Math.Max(0, width - point.X - DotToCardGap - margin);
        var leftCapacity = Math.Max(0, point.X - DotToCardGap - margin);
        var placeRight = rightCapacity >= CardMinWidth && (point.X <= width / 2 || leftCapacity < CardMinWidth);
        var sideCapacity = placeRight ? rightCapacity : leftCapacity;
        var cardWidth = Math.Max(Math.Min(desiredWidth, sideCapacity), Math.Min(CardMinWidth, sideCapacity));
        var cardX = placeRight
            ? point.X + DotToCardGap
            : point.X - DotToCardGap - cardWidth;

        cardX = Math.Clamp(cardX, margin, Math.Max(margin, width - cardWidth - margin));
        if (placeRight && cardX < point.X + DotToCardGap)
        {
            cardX = point.X + DotToCardGap;
        }
        else if (!placeRight && cardX + cardWidth > point.X - DotToCardGap)
        {
            cardX = point.X - DotToCardGap - cardWidth;
        }

        return new CardMetrics(cardX, cardWidth, Math.Max(80, cardWidth - CardPaddingX * 2));
    }

    private void RebuildEventPoints(IReadOnlyList<HistoryEventItem> events, double width)
    {
        _eventPoints.Clear();
        for (var index = 0; index < events.Count; index++)
        {
            var y = TopPadding;
            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                y += GetEventSpacing(events[previousIndex]);
            }

            _eventPoints[events[index].Id] = GetPoint(index, width, y);
        }
    }

    private static Point GetPoint(int index, double width, double y)
    {
        var lane = index % 6;
        var x = lane switch
        {
            0 => 120,
            1 => width * 0.44,
            2 => width - 160,
            3 => width - 160,
            4 => width * 0.56,
            _ => 120
        };

        return new Point(x, y);
    }

    private static double CalculateHeight(IReadOnlyList<HistoryEventItem> events)
    {
        if (events.Count == 0)
        {
            return 420;
        }

        var height = TopPadding + BottomPadding;
        for (var index = 0; index < events.Count; index++)
        {
            height += index == events.Count - 1
                ? GetLastEventHeight(events[index])
                : GetEventSpacing(events[index]);
        }

        return height;
    }

    private static double GetEventSpacing(HistoryEventItem historyEvent)
    {
        var lineCount = WrapText(historyEvent.Note, 60).Count;
        return Math.Max(MinimumStepY, 76 + lineCount * 18);
    }

    private static double GetLastEventHeight(HistoryEventItem historyEvent)
    {
        var lineCount = WrapText(historyEvent.Note, 60).Count;
        return Math.Max(120, 82 + lineCount * 18);
    }

    private static IReadOnlyList<string> WrapText(string text, int maxLineLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [" "];
        }

        var lines = new List<string>();
        var sourceLines = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var sourceLine in sourceLines)
        {
            var wrappedLines = WrapSingleLine(sourceLine, maxLineLength);
            lines.AddRange(wrappedLines);
        }

        return lines.Count == 0 ? [" "] : lines;
    }

    private static IReadOnlyList<string> WrapSingleLine(string text, int maxLineLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [" "];
        }

        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > maxLineLength)
            {
                if (!string.IsNullOrWhiteSpace(current))
                {
                    lines.Add(current);
                    current = string.Empty;
                }

                for (var index = 0; index < word.Length; index += maxLineLength)
                {
                    lines.Add(word.Substring(index, Math.Min(maxLineLength, word.Length - index)));
                }

                continue;
            }

            var candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
            if (candidate.Length <= maxLineLength)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            lines.Add(current);
        }

        return lines.Count == 0 ? [" "] : lines;
    }

    private static double CalculateHeight(int eventCount)
    {
        return eventCount == 0
            ? 420
            : TopPadding + BottomPadding + Math.Max(0, eventCount - 1) * MinimumStepY;
    }

    private IReadOnlyList<HistoryEventItem> GetEvents()
    {
        if (Events is null)
        {
            return [];
        }

        return Events
            .OfType<HistoryEventItem>()
            .OrderBy(item => item.EventDate)
            .ThenBy(item => item.CreatedAt)
            .ToList();
    }

    private Brush GetBrush(string key, Color fallbackColor)
    {
        return TryFindResource(key) as Brush ?? new SolidColorBrush(fallbackColor);
    }

    private readonly record struct CardMetrics(double CardX, double CardWidth, double TextWidth);
}
