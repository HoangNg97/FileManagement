using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LauncherCatalog.Models;
using LauncherCatalog.Services;
using LauncherCatalog.ViewModels;
using LauncherCatalog.Views;
using DrawingIcon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;
using Forms = System.Windows.Forms;

namespace LauncherCatalog;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly GlobalHotkeyService _hotkeyService = new();
    private HwndSource? _hwndSource;
    private QuickSearchWindow? _quickSearchWindow;
    private bool _isQuitting;

    public MainWindow()
    {
        InitializeComponent();

        var pathValidationService = new PathValidationService();
        var filePickerService = new FilePickerService();
        var entryDialogService = new EntryDialogService(pathValidationService, filePickerService);

        _viewModel = new MainViewModel(
            new EntryRepository(),
            new LauncherService(),
            pathValidationService,
            new IconCacheService(),
            new EverythingSearchService(),
            new ThemeService(),
            filePickerService,
            entryDialogService,
            () => this);

        DataContext = _viewModel;
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        _trayIcon = CreateTrayIcon();
        _hotkeyService.HotkeyPressed += (_, _) => ShowQuickSearch();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await _viewModel.LoadAsync();
        RegisterQuickSearchHotkey(showWarning: false);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var openItem = new Forms.ToolStripMenuItem("Open", null, (_, _) => RestoreFromTray());
        var quitItem = new Forms.ToolStripMenuItem("Quit", null, (_, _) => QuitFromTray());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(quitItem);

        var notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadTrayIcon(),
            Text = "Launcher Catalog",
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

    private void QuitFromTray()
    {
        _isQuitting = true;
        _trayIcon.Visible = false;
        Close();
    }

    private async void ShortcutSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeySettingsDialog(_viewModel.QuickSearchHotkeyText)
        {
            Owner = this
        };

        dialog.ShowDialog();
        if (!HotkeyGesture.TryParse(dialog.HotkeyText, out _))
        {
            System.Windows.MessageBox.Show(this, "That shortcut could not be registered.", "Launcher Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryRegisterQuickSearchHotkey(dialog.HotkeyText, showWarning: true))
        {
            return;
        }

        await _viewModel.UpdateQuickSearchHotkeyAsync(dialog.HotkeyText);
    }

    private void ShowQuickSearch()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsMainWindowOpen)
            {
                return;
            }

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

    private bool IsMainWindowOpen => IsVisible && WindowState != WindowState.Minimized;

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

        var message = $"Could not register quick search shortcut {gesture}. Another app may already use it.";
        _viewModel.StatusMessage = message;

        if (showWarning)
        {
            System.Windows.MessageBox.Show(this, message, "Launcher Catalog", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void ListBoxItem_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
        }
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
}
