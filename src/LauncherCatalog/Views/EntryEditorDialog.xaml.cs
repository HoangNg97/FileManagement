using System.Windows;
using System.Windows.Input;
using LauncherCatalog.Models;
using LauncherCatalog.Services;
using LauncherCatalog.ViewModels;

namespace LauncherCatalog.Views;

public partial class EntryEditorDialog : Window
{
    private readonly EntryEditorViewModel _viewModel;
    private readonly IFilePickerService _filePickerService;

    public EntryEditorDialog(
        CatalogEntry entry,
        PathValidationService pathValidationService,
        IFilePickerService filePickerService,
        IEnumerable<string> recentTags)
    {
        InitializeComponent();
        _filePickerService = filePickerService;
        _viewModel = new EntryEditorViewModel(entry, pathValidationService, recentTags);
        DataContext = _viewModel;
    }

    public CatalogEntry EditedEntry => _viewModel.ToEntry();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var path = _viewModel.Type == EntryType.App
            ? _filePickerService.PickExecutable(this, _viewModel.Path)
            : _filePickerService.PickFolder(this, _viewModel.Path);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _viewModel.Path = path;
        _viewModel.RefreshFromPath();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
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
