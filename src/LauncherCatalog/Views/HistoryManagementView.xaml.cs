using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LauncherCatalog.Models;
using LauncherCatalog.ViewModels;

namespace LauncherCatalog.Views;

public partial class HistoryManagementView : System.Windows.Controls.UserControl
{
    private readonly HistoryManagementViewModel _viewModel = new();
    private bool _hasLoaded;

    public HistoryManagementView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.ScrollToEventRequested += (_, eventId) => RouteMap.ScrollEventIntoView(eventId);
        RouteMap.EventClicked += RouteMap_EventClicked;
    }

    private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        await _viewModel.LoadAsync();
    }

    private async void CreateHistoryFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new HistoryFileEditorDialog
        {
            Owner = System.Windows.Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.CreateFileAsync(dialog.FileName, dialog.TagsText);
        }
    }

    private async void EditHistoryFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (GetHistoryFileFromMenuItem(sender) is not { } historyFile)
        {
            return;
        }

        _viewModel.SelectedFile = historyFile;
        var dialog = new HistoryFileEditorDialog(historyFile.Name, string.Join(", ", historyFile.Tags))
        {
            Owner = System.Windows.Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.UpdateFileAsync(historyFile, dialog.FileName, dialog.TagsText);
        }
    }

    private async void ToggleHistoryFileFavorite_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (GetHistoryFileFromMenuItem(sender) is { } historyFile)
        {
            _viewModel.SelectedFile = historyFile;
            await _viewModel.ToggleFileFavoriteAsync(historyFile);
        }
    }

    private async void DeleteHistoryFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (GetHistoryFileFromMenuItem(sender) is { } historyFile)
        {
            await _viewModel.DeleteFileAsync(historyFile);
        }
    }

    private async void NewEvent_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new HistoryEventEditorDialog
        {
            Owner = System.Windows.Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && dialog.EventDate is DateTime eventDate)
        {
            await _viewModel.CreateEventAsync(eventDate, dialog.Note);
        }
    }

    private async void RouteMap_EventClicked(object? sender, HistoryEventItem historyEvent)
    {
        var dialog = new HistoryEventEditorDialog(historyEvent.EventDate, historyEvent.Note)
        {
            Owner = System.Windows.Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.DeleteRequested)
        {
            await _viewModel.DeleteEventAsync(historyEvent);
            return;
        }

        if (dialog.EventDate is DateTime eventDate)
        {
            await _viewModel.UpdateEventAsync(historyEvent, eventDate, dialog.Note);
        }
    }

    private void ToggleEventSearch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.ShowEventSearch = !_viewModel.ShowEventSearch;
        if (_viewModel.ShowEventSearch)
        {
            Dispatcher.BeginInvoke(() =>
            {
                EventSearchTextBox.Focus();
                System.Windows.Input.Keyboard.Focus(EventSearchTextBox);
            });
        }
    }

    private void CloseEventSearch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.ShowEventSearch = false;
    }

    private void Root_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_viewModel.HasEventSearchResults
            || e.OriginalSource is not DependencyObject source
            || IsInsideElement(source, EventSearchBoxPanel)
            || IsInsideElement(source, EventSearchResultsPanel))
        {
            return;
        }

        _viewModel.DismissEventSearchResults();
    }

    private void ExportButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.ContextMenu is { } contextMenu)
        {
            contextMenu.PlacementTarget = button;
            contextMenu.IsOpen = true;
        }
    }

    private void ExportSelectedMonth_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel.ExportSelectedMonthCommand.CanExecute(null))
        {
            _viewModel.ExportSelectedMonthCommand.Execute(null);
        }
    }

    private void ExportAllEvents_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel.ExportAllEventsCommand.CanExecute(null))
        {
            _viewModel.ExportAllEventsCommand.Execute(null);
        }
    }

    private static HistoryFileItem? GetHistoryFileFromMenuItem(object sender)
    {
        if (sender is not MenuItem menuItem
            || menuItem.Parent is not ContextMenu contextMenu
            || contextMenu.PlacementTarget is not FrameworkElement target)
        {
            return null;
        }

        return target.DataContext as HistoryFileItem;
    }

    private static bool IsInsideElement(DependencyObject source, DependencyObject target)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
