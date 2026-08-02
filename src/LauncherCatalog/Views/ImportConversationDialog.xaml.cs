using System.Windows;
using System.Windows.Input;
using LauncherCatalog.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace LauncherCatalog.Views;

public partial class ImportConversationDialog : Window
{
    public ImportConversationDialog()
    {
        InitializeComponent();
        FileNameBox.Text = LocalizationService.Current.Get("Issues.Import.NoFile");
    }

    /// <summary>Absolute path of the JSON file the user chose, or null when nothing is selected.</summary>
    public string? SelectedFilePath { get; private set; }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SelectedFilePath = dialog.FileName;
        FileNameBox.Text = System.IO.Path.GetFileName(dialog.FileName);
        ImportButton.IsEnabled = true;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            return;
        }

        DialogResult = true;
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
