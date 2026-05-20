using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using LauncherCatalog.Services;

namespace LauncherCatalog.Views;

public partial class RestoreBackupDialog : Window
{
    public RestoreBackupDialog(IEnumerable<DatabaseBackupVersion> versions)
    {
        InitializeComponent();
        Versions = new ObservableCollection<DatabaseBackupVersion>(versions);
        SelectedVersion = Versions.FirstOrDefault();
        DataContext = this;
    }

    public ObservableCollection<DatabaseBackupVersion> Versions { get; }

    public DatabaseBackupVersion? SelectedVersion { get; set; }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedVersion is null)
        {
            System.Windows.MessageBox.Show(
                this,
                LocalizationService.Current.Get("Backup.RestoreDialog.SelectVersion"),
                LocalizationService.Current.Get("Common.AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
