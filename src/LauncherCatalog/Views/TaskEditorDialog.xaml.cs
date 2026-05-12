using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LauncherCatalog.Models;
using LauncherCatalog.ViewModels;

namespace LauncherCatalog.Views;

public partial class TaskEditorDialog : Window
{
    private readonly TaskEditorViewModel _viewModel;

    public TaskEditorDialog(TaskItem? task = null)
    {
        InitializeComponent();
        _viewModel = new TaskEditorViewModel(task);
        DataContext = _viewModel;
        DueDateCalendar.SelectedDate = _viewModel.DueDate;
        DueDatePopup.Closed += (_, _) => Focus();
    }

    public TaskItem CreatedTask => _viewModel.ToTask();

    private void DueDateField_Click(object sender, RoutedEventArgs e)
    {
        DueDateCalendar.SelectedDate = _viewModel.DueDate;
        DueDatePopup.IsOpen = true;
    }

    private void DueDateCalendar_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DueDateCalendar.SelectedDate is not DateTime selectedDate)
        {
            return;
        }

        _viewModel.DueDate = selectedDate.Date;
        DueDatePopup.IsOpen = false;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate(out var message))
        {
            System.Windows.MessageBox.Show(this, message, "Launcher Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void TitleClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
