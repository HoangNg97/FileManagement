using System.Windows;
using System.Windows.Input;
using LauncherCatalog.Services;

namespace LauncherCatalog.Views;

public partial class NewIssueDialog : Window
{
    /// <summary>Creates the dialog in "new issue" mode.</summary>
    public NewIssueDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>Creates the dialog in "edit issue" mode, pre-filled with the existing values.</summary>
    public NewIssueDialog(string existingTitle, string existingNote)
    {
        InitializeComponent();

        var localizer = LocalizationService.Current;
        Title = localizer.Get("Issues.EditIssue.Title");
        HeaderText.Text = localizer.Get("Issues.EditIssue.Title");
        PrimaryButton.Content = localizer.Get("Issues.Action.Save");

        TitleBox.Text = existingTitle;
        NoteBox.Text = existingNote;

        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    public string IssueTitle => TitleBox.Text.Trim();

    public string Note => NoteBox.Text.Trim();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            System.Windows.MessageBox.Show(
                this,
                LocalizationService.Current.Get("Issues.Validation.TitleRequired"),
                LocalizationService.Current.Get("Common.AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            TitleBox.Focus();
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
