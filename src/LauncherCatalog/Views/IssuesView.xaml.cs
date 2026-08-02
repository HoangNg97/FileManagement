using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using LauncherCatalog.Models;
using LauncherCatalog.Services;
using LauncherCatalog.ViewModels;

namespace LauncherCatalog.Views;

public partial class IssuesView : System.Windows.Controls.UserControl
{
    private readonly IssuesViewModel _viewModel = new();
    private bool _noteExpanded;

    public IssuesView()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.NewIssueRequested += OnNewIssueRequested;
        _viewModel.EditIssueRequested += OnEditIssueRequested;
        _viewModel.RemoveIssueRequested += OnRemoveIssueRequested;
        _viewModel.RemoveMessageRequested += OnRemoveMessageRequested;
        _viewModel.ValidationFailed += OnValidationFailed;
        _viewModel.ScrollToNewestRequested += OnScrollToNewestRequested;
        _viewModel.ScrollToMessageRequested += OnScrollToMessageRequested;
        _viewModel.ExportNotification += OnExportNotification;
        _viewModel.ImportConversationRequested += OnImportConversationRequested;
        _viewModel.ImportNotification += OnImportNotification;

        PreviewMouseDown += OnViewPreviewMouseDown;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.LoadAsync();
        ScrollMessagesToNewest();
    }

    private async void OnNewIssueRequested(object? sender, EventArgs e)
    {
        var dialog = new NewIssueDialog
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.CreateIssueAsync(dialog.IssueTitle, dialog.Note);
        }
    }

    private async void OnEditIssueRequested(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedIssue is not { } issue)
        {
            return;
        }

        var dialog = new NewIssueDialog(issue.Title, issue.Note)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.UpdateIssueAsync(dialog.IssueTitle, dialog.Note);
        }
    }

    private async void OnRemoveIssueRequested(object? sender, EventArgs e)
    {
        var localizer = LocalizationService.Current;
        var result = System.Windows.MessageBox.Show(
            Window.GetWindow(this)!,
            localizer.Get("Issues.RemoveIssue.Confirm"),
            localizer.Get("Issues.RemoveIssue.Title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK)
        {
            await _viewModel.RemoveSelectedIssueAsync();
        }
    }

    private async void OnImportConversationRequested(object? sender, EventArgs e)
    {
        var dialog = new ImportConversationDialog
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedFilePath))
        {
            await _viewModel.ImportConversationAsync(dialog.SelectedFilePath);
        }
    }

    private async void OnRemoveMessageRequested(object? sender, IssueMessage message)
    {
        var localizer = LocalizationService.Current;
        var result = System.Windows.MessageBox.Show(
            Window.GetWindow(this)!,
            localizer.Get("Issues.Remove.Confirm"),
            localizer.Get("Issues.Remove.Title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK)
        {
            await _viewModel.RemoveMessageAsync(message);
        }
    }

    private void OnValidationFailed(object? sender, string message)
    {
        System.Windows.MessageBox.Show(
            Window.GetWindow(this)!,
            message,
            LocalizationService.Current.Get("Common.AppName"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnScrollToNewestRequested(object? sender, EventArgs e)
    {
        ScrollMessagesToNewest();
    }

    private void OnScrollToMessageRequested(object? sender, IssueMessage message)
    {
        ScrollToMessage(message);
    }

    private void OnExportNotification(object? sender, string message)
    {
        System.Windows.MessageBox.Show(
            Window.GetWindow(this)!,
            message,
            LocalizationService.Current.Get("Common.AppName"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnImportNotification(object? sender, string message)
    {
        System.Windows.MessageBox.Show(
            Window.GetWindow(this)!,
            message,
            LocalizationService.Current.Get("Common.AppName"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void NewMessageDateField_Click(object sender, RoutedEventArgs e)
    {
        NewMessageDateCalendar.SelectedDate = _viewModel.NewMessageCreatedDate.Date;
        NewMessageDatePopup.IsOpen = true;
    }

    private void NewMessageDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NewMessageDateCalendar.SelectedDate is { } date)
        {
            _viewModel.SetNewMessageDate(date);
            NewMessageDatePopup.IsOpen = false;
        }
    }

    private void EditMessageDateField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement source)
        {
            return;
        }

        var popup = FindPopupSibling(source);
        if (popup is not null)
        {
            popup.IsOpen = true;
        }
    }

    private void EditMessageDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not Calendar calendar
            || calendar.DataContext is not IssueMessage message
            || calendar.SelectedDate is not { } date)
        {
            return;
        }

        _viewModel.SetEditMessageDate(message, date);

        var popup = FindAncestor<Popup>(calendar);
        if (popup is not null)
        {
            popup.IsOpen = false;
        }
    }

    private void IssueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.UpdateSelectedIssues(IssueList.SelectedItems.OfType<Issue>());
        ScrollMessagesToNewest();
    }

    private void IssueList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Right-click should target the clicked row (Explorer-style). If it is already part of a
        // multi-selection, leave the selection intact so a right-click export still covers them all.
        var source = e.OriginalSource as DependencyObject;
        var container = ItemsControl.ContainerFromElement(IssueList, source) as ListBoxItem;
        if (container is null)
        {
            return;
        }

        if (!container.IsSelected)
        {
            IssueList.SelectedItems.Clear();
            container.IsSelected = true;
        }
    }

    private void ScrollMessagesToNewest()
    {
        // Newest message renders at the top, so scroll the thread up to reveal the latest item.
        Dispatcher.BeginInvoke(new Action(() => MessagesScroll?.ScrollToTop()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ScrollToMessage(IssueMessage message)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FindDescendantByDataContext(MessagesScroll, message) is FrameworkElement element)
            {
                element.BringIntoView();
            }
            else
            {
                // Message visuals can be recreated during issue-list refresh; retry once after UI idle.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (FindDescendantByDataContext(MessagesScroll, message) is FrameworkElement retry)
                    {
                        retry.BringIntoView();
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void NotePanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking the note expands it. The event is left unhandled so text selection and
        // link navigation inside the note keep working.
        if (!_noteExpanded)
        {
            _noteExpanded = true;
            RefreshNoteLayout();
        }
    }

    private void OnViewPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Collapse the note back to two lines when the user clicks anywhere outside of it.
        if (_noteExpanded && !IsWithin(e.OriginalSource, NotePanel))
        {
            _noteExpanded = false;
            RefreshNoteLayout();
        }
    }

    private void NoteTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // A different note is being shown (e.g. another issue selected) — always start collapsed.
        _noteExpanded = false;
        Dispatcher.BeginInvoke(new Action(RefreshNoteLayout),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void NoteTextBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Wrapping may have changed with the width; re-evaluate the expand affordance.
        if (e.WidthChanged)
        {
            Dispatcher.BeginInvoke(new Action(RefreshNoteLayout),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void NoteTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // While collapsed the note is clipped, not scrollable — swallow the wheel so hidden
        // lines stay hidden until the user expands the note.
        if (!_noteExpanded)
        {
            e.Handled = true;
        }
    }

    private void RefreshNoteLayout()
    {
        if (NoteTextBox is null || NoteExpandChevron is null)
        {
            return;
        }

        if (_noteExpanded)
        {
            // Let the note grow, but cap it so a very long note cannot crowd out the messages.
            double cap = RightPanelGrid.ActualHeight > 0
                ? RightPanelGrid.ActualHeight * 0.5
                : double.PositiveInfinity;
            NoteTextBox.MaxHeight = cap;
            NoteTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            NoteExpandChevron.Visibility = Visibility.Collapsed;
            return;
        }

        NoteTextBox.MaxHeight = CollapsedNoteHeight();
        NoteTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

        // Measure after layout settles so ExtentHeight reflects the full (clipped) content.
        Dispatcher.BeginInvoke(new Action(UpdateNoteChevron),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateNoteChevron()
    {
        if (NoteTextBox is null || NoteExpandChevron is null || _noteExpanded)
        {
            return;
        }

        var scrollViewer = FindDescendantScrollViewer(NoteTextBox);
        bool overflows = scrollViewer is not null &&
                         scrollViewer.ExtentHeight > scrollViewer.ViewportHeight + 1;
        NoteExpandChevron.Visibility = overflows ? Visibility.Visible : Visibility.Collapsed;
    }

    private double CollapsedNoteHeight()
    {
        double lineHeight = NoteTextBox.FontFamily.LineSpacing * NoteTextBox.FontSize;
        return Math.Ceiling(lineHeight * 2) + 2;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var nested = FindDescendantScrollViewer(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static DependencyObject? FindDescendantByDataContext(DependencyObject root, object dataContext)
    {
        if (root is FrameworkElement element && ReferenceEquals(element.DataContext, dataContext))
        {
            return root;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindDescendantByDataContext(VisualTreeHelper.GetChild(root, i), dataContext);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsWithin(object? source, DependencyObject ancestor)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            DependencyObject? parent = null;
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                parent = VisualTreeHelper.GetParent(current);
            }

            parent ??= LogicalTreeHelper.GetParent(current);

            if (parent is null && current is FrameworkContentElement contentElement)
            {
                parent = contentElement.Parent;
            }

            current = parent;
        }

        return false;
    }

    private static Popup? FindPopupSibling(FrameworkElement source)
    {
        if (source.Parent is not DependencyObject parent)
        {
            return null;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is Popup popup)
            {
                return popup;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            DependencyObject? parent = null;
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                parent = VisualTreeHelper.GetParent(current);
            }

            parent ??= LogicalTreeHelper.GetParent(current);

            if (parent is null && current is FrameworkContentElement content)
            {
                parent = content.Parent;
            }

            current = parent;
        }

        return null;
    }
}
