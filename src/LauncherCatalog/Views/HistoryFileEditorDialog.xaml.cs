using System.Windows;
using System.Windows.Input;
using LauncherCatalog.Services;

namespace LauncherCatalog.Views;

public partial class HistoryFileEditorDialog : Window
{
    public static readonly DependencyProperty DialogTitleProperty =
        DependencyProperty.Register(
            nameof(DialogTitle),
            typeof(string),
            typeof(HistoryFileEditorDialog),
            new PropertyMetadata(LocalizationService.Current.Get("History.Dialog.CreateTitle")));

    public static readonly DependencyProperty DialogSubtitleProperty =
        DependencyProperty.Register(
            nameof(DialogSubtitle),
            typeof(string),
            typeof(HistoryFileEditorDialog),
            new PropertyMetadata(LocalizationService.Current.Get("History.Dialog.CreateSubtitle")));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(
            nameof(ActionText),
            typeof(string),
            typeof(HistoryFileEditorDialog),
            new PropertyMetadata(LocalizationService.Current.Get("History.Action.CreateFile")));

    public static readonly DependencyProperty FileNameProperty =
        DependencyProperty.Register(
            nameof(FileName),
            typeof(string),
            typeof(HistoryFileEditorDialog),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TagsTextProperty =
        DependencyProperty.Register(
            nameof(TagsText),
            typeof(string),
            typeof(HistoryFileEditorDialog),
            new PropertyMetadata(string.Empty));

    public HistoryFileEditorDialog()
    {
        InitializeComponent();
    }

    public HistoryFileEditorDialog(string fileName, string tagsText)
        : this()
    {
        FileName = fileName;
        TagsText = tagsText;
        DialogTitle = LocalizationService.Current.Get("History.Dialog.EditTitle");
        DialogSubtitle = LocalizationService.Current.Get("History.Dialog.EditSubtitle");
        ActionText = LocalizationService.Current.Get("Common.Save");
    }

    public string DialogTitle
    {
        get => (string)GetValue(DialogTitleProperty);
        set => SetValue(DialogTitleProperty, value);
    }

    public string DialogSubtitle
    {
        get => (string)GetValue(DialogSubtitleProperty);
        set => SetValue(DialogSubtitleProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public string FileName
    {
        get => (string)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    public string TagsText
    {
        get => (string)GetValue(TagsTextProperty);
        set => SetValue(TagsTextProperty, value);
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FileName))
        {
            ValidationText.Text = LocalizationService.Current.Get("History.Message.FileNameRequired");
            ValidationText.Visibility = Visibility.Visible;
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
        if (e.ClickCount == 2)
        {
            return;
        }

        DragMove();
    }
}
