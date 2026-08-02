using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LauncherCatalog.Models;
using LauncherCatalog.Services;
using LauncherCatalog.Services.Ai;
using LauncherCatalog.ViewModels;
using LauncherCatalog.Views;
using DrawingIcon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;
using Forms = System.Windows.Forms;

namespace LauncherCatalog;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TaskRepository _taskRepository;
    private readonly EverythingSearchService _everythingSearchService;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly TaskReminderService _taskReminderService;
    private readonly DatabaseBackupService _databaseBackupService;
    private readonly StartupLaunchService _startupLaunchService;
    private readonly GlobalHotkeyService _hotkeyService = new();
    private readonly ChatViewModel _chatViewModel;
    private bool _isChatOpen;
    private HwndSource? _hwndSource;
    private QuickSearchWindow? _quickSearchWindow;
    private bool _isQuitting;
    private bool _lastBalloonWasSignIn;

    public MainWindow()
    {
        InitializeComponent();

        var pathValidationService = new PathValidationService();
        var filePickerService = new FilePickerService();
        var entryDialogService = new EntryDialogService(pathValidationService, filePickerService);
        var entryRepository = new EntryRepository();
        var taskRepository = new TaskRepository();
        var noteRepository = new NoteRepository();
        var issueRepository = new IssueRepository();
        var historyRepository = new HistoryRepository();
        _everythingSearchService = new EverythingSearchService();

        _viewModel = new MainViewModel(
            entryRepository,
            taskRepository,
            noteRepository,
            new LauncherService(),
            pathValidationService,
            new IconCacheService(),
            _everythingSearchService,
            new ThemeService(),
            filePickerService,
            entryDialogService,
            new TaskDialogService(),
            () => this);
        _taskRepository = taskRepository;
        _databaseBackupService = new DatabaseBackupService(
            Path.GetDirectoryName(entryRepository.StoragePath) ?? string.Empty,
            [
                DatabaseBackupTarget.FromPath(entryRepository.StoragePath),
                DatabaseBackupTarget.FromPath(taskRepository.StoragePath),
                DatabaseBackupTarget.FromPath(noteRepository.StoragePath),
                DatabaseBackupTarget.FromPath(issueRepository.StoragePath),
                DatabaseBackupTarget.FromPath(HistoryRepository.DefaultStoragePath)
            ]);
        _startupLaunchService = new StartupLaunchService();

        _chatViewModel = new ChatViewModel(
            AiBridgeManager.Current,
            new ChatContextExporter(entryRepository, taskRepository, noteRepository, historyRepository, issueRepository),
            GetActiveChatContextTab);
        ChatPanel.AttachViewModel(_chatViewModel);
        ChatPanel.CloseRequested += (_, _) => HideChatPanel();
        MainTabs.SelectionChanged += MainTabs_SelectionChanged;
        // Close the chat overlay when the user clicks anywhere outside it (except the toggle button,
        // which already handles open/close on its own). PreviewMouseDown catches the click before
        // controls consume it, and Deactivated covers alt-tab / clicking outside the whole app.
        PreviewMouseDown += MainWindow_PreviewMouseDown;
        Deactivated += (_, _) => { if (_isChatOpen) HideChatPanel(); };

        DataContext = _viewModel;
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        _trayIcon = CreateTrayIcon();
        _taskReminderService = new TaskReminderService(
            _taskRepository,
            () => _viewModel.GetAllTasksSnapshot(),
            ShowTaskReminderNotification);
        _hotkeyService.HotkeyPressed += (_, _) => ShowQuickSearch();
        AiBridgeManager.Current.SignInRequired += OnGeminiSignInRequired;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        try
        {
            await _taskRepository.EnsureInitializedAsync();
        }
        catch (Exception exception)
        {
            AppLogger.WriteInfo($"Task database initialization failed: {exception.Message}");
        }

        await _viewModel.LoadAsync();
        _databaseBackupService.Start();
        _startupLaunchService.EnsureEnabled();
        _ = _everythingSearchService.EnsureStartedAsync();
        _taskReminderService.Start();
        RegisterQuickSearchHotkey(showWarning: false);
        await EnsureAiBridgeStartedAsync();
    }

    private async Task EnsureAiBridgeStartedAsync()
    {
        try
        {
            await AiBridgeManager.Current.InitializeAsync();
        }
        catch (Exception exception)
        {
            AppLogger.WriteInfo($"AI bridge failed to start: {exception.Message}");
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var localizer = LocalizationService.Current;
        var openItem = new Forms.ToolStripMenuItem(localizer.Get("Tray.Open"), null, (_, _) => RestoreFromTray());
        var quitItem = new Forms.ToolStripMenuItem(localizer.Get("Tray.Quit"), null, (_, _) => QuitFromTray());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(quitItem);

        var notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadTrayIcon(),
            Text = localizer.Get("Common.AppName"),
            Visible = true
        };

        notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };
        notifyIcon.BalloonTipClicked += (_, _) => OnBalloonTipClicked();

        return notifyIcon;
    }

    private static DrawingIcon LoadTrayIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName;

            if (!string.IsNullOrWhiteSpace(executablePath) && DrawingIcon.ExtractAssociatedIcon(executablePath) is { } icon)
            {
                return icon;
            }
        }
        catch
        {
            // Fall back to the system application icon if the executable icon cannot be read.
        }

        return SystemIcons.Application;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isQuitting)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        _hotkeyService.Dispose();
        _taskReminderService.Dispose();
        _databaseBackupService.Dispose();
        AiBridgeManager.Current.SignInRequired -= OnGeminiSignInRequired;
        try
        {
            AiBridgeManager.Current.ShutdownAsync().Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception exception)
        {
            AppLogger.WriteInfo($"AI bridge shutdown error: {exception.Message}");
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
    }

    public void ActivateFromExternalInstance()
    {
        RestoreFromTray();
    }

    private void ShowTaskReminderNotification(string title, string message)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _lastBalloonWasSignIn = false;

        // Keep task reminders visible in Windows notification center until the user clears them,
        // matching the persistent behavior used by the Gemini sign-in warning.
        _trayIcon.ShowBalloonTip(30000);
    }

    /// <summary>
    /// Fired (on a worker thread) when a Gemini request was rejected because the session was silently
    /// signed out. Marshals to the UI thread and shows a tray notification that persists in the
    /// Windows notification center until dismissed, so the user notices even if the app is minimized.
    /// </summary>
    private void OnGeminiSignInRequired(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ShowGeminiSignInRequiredNotification));
    }

    private void ShowGeminiSignInRequiredNotification()
    {
        var localizer = LocalizationService.Current;
        _trayIcon.BalloonTipTitle = localizer.Get("AiBridge.SignInRequired.Title");
        _trayIcon.BalloonTipText = localizer.Get("AiBridge.SignInRequired.Message");
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _lastBalloonWasSignIn = true;

        // On Windows 10/11 the toast is shown briefly and then kept in the notification center
        // until the user dismisses it, so it is not lost while the window is minimized.
        _trayIcon.ShowBalloonTip(30000);
    }

    private void OnBalloonTipClicked()
    {
        RestoreFromTray();

        // If the last notification was the Gemini sign-in prompt, take the user straight to the
        // AI Bridge tab where the "Log in to Gemini" action lives.
        if (_lastBalloonWasSignIn && AiBridgeTab is not null)
        {
            AiBridgeTab.IsSelected = true;
        }
    }

    private void QuitFromTray()
    {
        _isQuitting = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_viewModel, ApplyQuickSearchHotkeyAsync)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private void Chat_Click(object sender, RoutedEventArgs e)
    {
        if (_isChatOpen)
        {
            HideChatPanel();
        }
        else
        {
            ShowChatPanel();
        }
    }

    private void ShowChatPanel()
    {
        _chatViewModel.RefreshActiveTab();
        ChatPanel.ShowAnimated();
        _isChatOpen = true;
    }

    private void HideChatPanel()
    {
        ChatPanel.HideAnimated();
        _isChatOpen = false;
    }

    /// <summary>
    /// Collapses the chat overlay when the user clicks anywhere that is not inside the panel or on
    /// the chat toggle button (which handles its own open/close). Walks the visual tree from the
    /// click's original source; if neither the panel nor the button is an ancestor, hides the panel.
    /// </summary>
    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isChatOpen)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        for (var node = source; node is not null; node = VisualTreeHelperParent(node))
        {
            if (ReferenceEquals(node, ChatPanel) || ReferenceEquals(node, ChatButton))
            {
                return;
            }
        }

        HideChatPanel();
    }

    private static DependencyObject? VisualTreeHelperParent(DependencyObject node)
    {
        // Prefer the visual parent so popups/tooltips still resolve to the panel that owns them;
        // fall back to the logical parent for non-visual descendants (e.g. FlowDocument runs).
        return node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
    }

    /// <summary>
    /// Maps the currently-selected tab in <see cref="MainTabs"/> to a <see cref="ChatContextTab"/>.
    /// Tab order in <c>MainWindow.xaml</c>: Catalog, Tasks, Note, History Management, Issues, AI Bridge.
    /// AI Bridge itself does not participate in chat context, so it maps to <c>Unsupported</c>.
    /// </summary>
    private ChatContextTab GetActiveChatContextTab()
    {
        return MainTabs.SelectedIndex switch
        {
            0 => ChatContextTab.Catalog,
            1 => ChatContextTab.Tasks,
            2 => ChatContextTab.Note,
            3 => ChatContextTab.HistoryManagement,
            4 => ChatContextTab.Issues,
            _ => ChatContextTab.Unsupported
        };
    }

    /// <summary>
    /// Keeps the chat panel's header/placeholder/empty-state in sync with the currently focused
    /// tab while the panel is open. Ignored bubbles coming from nested TabControls inside a view
    /// by filtering on the sender.
    /// </summary>
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs))
        {
            return;
        }
        if (_isChatOpen)
        {
            _chatViewModel.RefreshActiveTab();
        }
    }

    /// <summary>
    /// Validates, registers, and persists a candidate quick-search shortcut. Returns whether it was
    /// applied; used by the Settings window so hotkey OS registration stays where the window handle is.
    /// </summary>
    private async Task<bool> ApplyQuickSearchHotkeyAsync(string hotkeyText)
    {
        if (!HotkeyGesture.TryParse(hotkeyText, out _))
        {
            System.Windows.MessageBox.Show(this, LocalizationService.Current.Get("Settings.Hotkey.Invalid"), LocalizationService.Current.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!TryRegisterQuickSearchHotkey(hotkeyText, showWarning: true))
        {
            return false;
        }

        await _viewModel.UpdateQuickSearchHotkeyAsync(hotkeyText);
        return true;
    }

    private async void RestoreLatestBackup_Click(object sender, RoutedEventArgs e)
    {
        var localizer = LocalizationService.Current;
        var availableVersions = _databaseBackupService.GetAvailableBackupVersions();
        if (availableVersions.Count == 0)
        {
            var noBackupMessage = localizer.Get("Backup.RestoreLatest.NoBackup");
            _viewModel.StatusMessage = noBackupMessage;
            System.Windows.MessageBox.Show(this, noBackupMessage, localizer.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new RestoreBackupDialog(availableVersions)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.SelectedVersion is null)
        {
            return;
        }

        _viewModel.PrepareForDatabaseRestore();
        _taskReminderService.Stop();
        _databaseBackupService.Stop();

        try
        {
            var result = await _databaseBackupService.RestoreAsync(dialog.SelectedVersion.VersionName);
            if (!result.IsSuccess)
            {
                var noBackupMessage = localizer.Get("Backup.RestoreLatest.NoBackup");
                _viewModel.StatusMessage = noBackupMessage;
                System.Windows.MessageBox.Show(this, noBackupMessage, localizer.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await _viewModel.LoadAsync();
            RegisterQuickSearchHotkey(showWarning: false);

            var successMessage = localizer.Format("Backup.RestoreLatest.Success", result.VersionName ?? string.Empty);
            _viewModel.StatusMessage = successMessage;
            System.Windows.MessageBox.Show(this, successMessage, localizer.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppLogger.WriteInfo($"Latest backup restore failed: {exception.Message}");
            var failureMessage = localizer.Format("Backup.RestoreLatest.Failed", exception.Message);
            _viewModel.StatusMessage = failureMessage;
            System.Windows.MessageBox.Show(this, failureMessage, localizer.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _taskReminderService.Start();
            _databaseBackupService.Start();
        }
    }

    private void ShowQuickSearch()
    {
        Dispatcher.Invoke(() =>
        {
            if (_quickSearchWindow is { IsVisible: true })
            {
                _quickSearchWindow.Activate();
                _quickSearchWindow.FocusSearch();
                return;
            }

            _quickSearchWindow = new QuickSearchWindow(_viewModel);
            _quickSearchWindow.Closed += (_, _) => _quickSearchWindow = null;
            _quickSearchWindow.Show();
            _quickSearchWindow.Activate();
            _quickSearchWindow.FocusSearch();
        });
    }

    private void RegisterQuickSearchHotkey(bool showWarning)
    {
        TryRegisterQuickSearchHotkey(_viewModel.QuickSearchHotkeyText, showWarning);
    }

    private bool TryRegisterQuickSearchHotkey(string hotkeyText, bool showWarning)
    {
        if (!HotkeyGesture.TryParse(hotkeyText, out var gesture))
        {
            return false;
        }

        var handle = new WindowInteropHelper(this).Handle;
        var registered = _hotkeyService.Register(handle, gesture);
        if (registered)
        {
            return true;
        }

        var message = LocalizationService.Current.Format("Settings.Hotkey.InUse", gesture.ToDisplayString());
        _viewModel.StatusMessage = message;

        if (showWarning)
        {
            System.Windows.MessageBox.Show(this, message, LocalizationService.Current.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        return _hotkeyService.HandleWndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && FindAncestor<ListBoxItem>(source)?.DataContext is CatalogEntry entry
            && _viewModel.OpenEntryCommand.CanExecute(entry))
        {
            _viewModel.OpenEntryCommand.Execute(entry);
        }
    }

    private void ResultsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = FindAncestor<ScrollViewer>(source);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        var visibleEntries = _viewModel.EntriesView.Cast<CatalogEntry>().ToList();
        if (visibleEntries.Count == 0)
        {
            return;
        }

        var currentIndex = _viewModel.SelectedEntry is null
            ? -1
            : visibleEntries.FindIndex(entry => entry.Id == _viewModel.SelectedEntry.Id);
        var nextIndex = e.Key == Key.Down
            ? Math.Min(currentIndex + 1, visibleEntries.Count - 1)
            : currentIndex < 0
                ? visibleEntries.Count - 1
                : Math.Max(currentIndex - 1, 0);

        _viewModel.SelectedEntry = visibleEntries[nextIndex];
        EntriesList.ScrollIntoView(_viewModel.SelectedEntry);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static Border? FindTaskRowBorder(DependencyObject source)
    {
        while (source is not null)
        {
            if (source is Border border && Equals(border.Tag, "TaskRowRoot"))
            {
                return border;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaxRestore_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private async void RemoveTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not TaskItem task)
        {
            return;
        }

        var rowBorder = FindTaskRowBorder(element);
        if (rowBorder is null)
        {
            await _viewModel.DeleteTaskAsync(task);
            return;
        }

        var originalHeight = rowBorder.ActualHeight;
        var originalMargin = rowBorder.Margin;

        try
        {
            await AnimateTaskRemovalAsync(rowBorder);
            await _viewModel.DeleteTaskAsync(task);
        }
        catch (Exception exception)
        {
            RestoreTaskRow(rowBorder, originalHeight, originalMargin);
            AppLogger.WriteInfo($"Task removal failed: {exception.Message}");
            System.Windows.MessageBox.Show(this, LocalizationService.Current.Get("Task.Message.RemoveFailed"), LocalizationService.Current.Get("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void TaskRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border rowBorder
            || rowBorder.DataContext is not TaskItem task
            || !task.ShouldAnimateIn)
        {
            return;
        }

        task.ShouldAnimateIn = false;
        rowBorder.Opacity = 0;
        rowBorder.RenderTransform = new TranslateTransform(0, 10);

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(180));

        var opacityAnimation = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnimation, rowBorder);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(opacityAnimation);

        var slideAnimation = new DoubleAnimation(10, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideAnimation, rowBorder);
        Storyboard.SetTargetProperty(slideAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        storyboard.Children.Add(slideAnimation);

        storyboard.Begin();
    }

    private void TaskTitleEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox
            || textBox.DataContext is not TaskItem task
            || !task.ShowInlineEditor
            || !textBox.IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async void TaskTitleEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox
            || textBox.DataContext is not TaskItem task)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await _viewModel.CommitTaskTitleEditAsync(task);
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _viewModel.CancelTaskTitleEdit(task);
        }
    }

    private async void TaskTitleEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox
            || textBox.DataContext is not TaskItem task
            || !task.ShowInlineEditor
            || !textBox.IsVisible)
        {
            return;
        }

        await _viewModel.CommitTaskTitleEditAsync(task);
    }

    private async void TaskStatusOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.DataContext is not TaskItem task
            || element.Tag is not string rawStatus
            || !Enum.TryParse<LauncherCatalog.Models.TaskStatus>(rawStatus, ignoreCase: true, out var status))
        {
            return;
        }

        await _viewModel.UpdateTaskStatusAsync(task, status);
        if (FindAncestor<Popup>(element) is { } popup)
        {
            popup.IsOpen = false;
        }
    }

    private async void TaskDueDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not Calendar calendar
            || calendar.DataContext is not TaskItem task
            || calendar.SelectedDate is not DateTime selectedDate)
        {
            return;
        }

        if (task.DueDate?.Date == selectedDate.Date)
        {
            if (FindAncestor<Popup>(calendar) is { } unchangedPopup)
            {
                unchangedPopup.IsOpen = false;
            }
            return;
        }

        await _viewModel.UpdateTaskDueDateAsync(task, selectedDate.Date);
        if (FindAncestor<Popup>(calendar) is { } popup)
        {
            popup.IsOpen = false;
        }
    }

    private static Task AnimateTaskRemovalAsync(Border rowBorder)
    {
        rowBorder.UpdateLayout();

        var originalHeight = rowBorder.ActualHeight;
        rowBorder.Height = originalHeight;
        rowBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        rowBorder.RenderTransform = new TranslateTransform();

        var storyboard = new Storyboard
        {
            FillBehavior = FillBehavior.HoldEnd
        };

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var opacityAnimation = new DoubleAnimation(1, 0, duration)
        {
            EasingFunction = ease
        };
        Storyboard.SetTarget(opacityAnimation, rowBorder);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));

        var slideAnimation = new DoubleAnimation(0, 22, duration)
        {
            EasingFunction = ease
        };
        Storyboard.SetTarget(slideAnimation, rowBorder);
        Storyboard.SetTargetProperty(slideAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

        var heightAnimation = new DoubleAnimation(originalHeight, 0, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(40),
            EasingFunction = ease
        };
        Storyboard.SetTarget(heightAnimation, rowBorder);
        Storyboard.SetTargetProperty(heightAnimation, new PropertyPath(FrameworkElement.HeightProperty));

        var marginAnimation = new ThicknessAnimation(rowBorder.Margin, new Thickness(rowBorder.Margin.Left, rowBorder.Margin.Top, rowBorder.Margin.Right, 0), duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(40),
            EasingFunction = ease
        };
        Storyboard.SetTarget(marginAnimation, rowBorder);
        Storyboard.SetTargetProperty(marginAnimation, new PropertyPath(FrameworkElement.MarginProperty));

        storyboard.Children.Add(opacityAnimation);
        storyboard.Children.Add(slideAnimation);
        storyboard.Children.Add(heightAnimation);
        storyboard.Children.Add(marginAnimation);

        var tcs = new TaskCompletionSource();
        storyboard.Completed += (_, _) => tcs.TrySetResult();
        storyboard.Begin(rowBorder);
        return tcs.Task;
    }

    private static void RestoreTaskRow(Border rowBorder, double originalHeight, Thickness originalMargin)
    {
        rowBorder.BeginAnimation(UIElement.OpacityProperty, null);
        rowBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
        rowBorder.BeginAnimation(FrameworkElement.MarginProperty, null);
        if (rowBorder.RenderTransform is TranslateTransform translateTransform)
        {
            translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            translateTransform.X = 0;
        }

        rowBorder.Opacity = 1;
        rowBorder.Height = originalHeight;
        rowBorder.Margin = originalMargin;
    }
}
