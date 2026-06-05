using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LauncherCatalog.Services;

namespace LauncherCatalog.Views;

public partial class HistoryEventEditorDialog : Window
{
    public static readonly DependencyProperty DialogTitleProperty =
        DependencyProperty.Register(
            nameof(DialogTitle),
            typeof(string),
            typeof(HistoryEventEditorDialog),
            new PropertyMetadata(LocalizationService.Current.Get("History.Event.NewTitle")));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(
            nameof(ActionText),
            typeof(string),
            typeof(HistoryEventEditorDialog),
            new PropertyMetadata(LocalizationService.Current.Get("Common.Save")));

    public static readonly DependencyProperty EventDateProperty =
        DependencyProperty.Register(
            nameof(EventDate),
            typeof(DateTime?),
            typeof(HistoryEventEditorDialog),
            new PropertyMetadata(DateTime.Today, OnEventDateChanged));

    public static readonly DependencyProperty NoteProperty =
        DependencyProperty.Register(
            nameof(Note),
            typeof(string),
            typeof(HistoryEventEditorDialog),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowDeleteButtonProperty =
        DependencyProperty.Register(
            nameof(ShowDeleteButton),
            typeof(bool),
            typeof(HistoryEventEditorDialog),
            new PropertyMetadata(false));

    public HistoryEventEditorDialog()
    {
        InitializeComponent();
        EventDateCalendar.SelectedDate = EventDate;
        EventDatePopup.Closed += (_, _) => Focus();
    }

    public HistoryEventEditorDialog(DateTime eventDate, string note)
        : this()
    {
        DialogTitle = LocalizationService.Current.Get("History.Event.EditTitle");
        EventDate = eventDate;
        Note = note;
        ShowDeleteButton = true;
    }

    public bool DeleteRequested { get; private set; }

    public string DialogTitle
    {
        get => (string)GetValue(DialogTitleProperty);
        set => SetValue(DialogTitleProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public DateTime? EventDate
    {
        get => (DateTime?)GetValue(EventDateProperty);
        set => SetValue(EventDateProperty, value);
    }

    public string EventDateText => EventDate?.ToString("d") ?? string.Empty;

    public string Note
    {
        get => (string)GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public bool ShowDeleteButton
    {
        get => (bool)GetValue(ShowDeleteButtonProperty);
        set => SetValue(ShowDeleteButtonProperty, value);
    }

    private static void OnEventDateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is HistoryEventEditorDialog dialog)
        {
            dialog.EventDateCalendar.SelectedDate = dialog.EventDate;
            dialog.OnEventDateTextChanged();
        }
    }

    private void OnEventDateTextChanged()
    {
        EventDateField.GetBindingExpression(ContentControl.ContentProperty)?.UpdateTarget();
    }

    private void EventDateField_Click(object sender, RoutedEventArgs e)
    {
        EventDateCalendar.SelectedDate = EventDate;
        EventDatePopup.IsOpen = true;
    }

    private void EventDateCalendar_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EventDateCalendar.SelectedDate is not DateTime selectedDate)
        {
            return;
        }

        EventDate = selectedDate.Date;
        EventDatePopup.IsOpen = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (EventDate is null)
        {
            ValidationText.Text = LocalizationService.Current.Get("History.Message.EventDateRequired");
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(Note))
        {
            ValidationText.Text = LocalizationService.Current.Get("History.Message.EventNoteRequired");
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        DialogResult = true;
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
