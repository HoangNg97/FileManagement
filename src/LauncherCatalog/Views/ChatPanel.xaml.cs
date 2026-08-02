using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LauncherCatalog.Models;
using LauncherCatalog.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace LauncherCatalog.Views;

/// <summary>
/// The in-app Gemini chat panel. Hosted as an overlay in <c>MainWindow</c>; open with
/// <see cref="ShowAnimated"/> and close with <see cref="HideAnimated"/> so the panel scales up from
/// its top-right origin (the chat button) like an Android bubble tap.
/// </summary>
public partial class ChatPanel : UserControl
{
    public ChatPanel()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        DataContextChanged += ChatPanel_DataContextChanged;
    }

    public event EventHandler? CloseRequested;

    public void AttachViewModel(ChatViewModel viewModel)
    {
        DataContext = viewModel;
    }

    /// <summary>Refreshes localized labels + the active tab (call whenever the panel becomes visible).</summary>
    public void RefreshActiveTab()
    {
        if (DataContext is ChatViewModel vm)
        {
            vm.RefreshActiveTab();
        }
    }

    /// <summary>
    /// Fades and scales the panel in. Called by <c>MainWindow</c> after positioning the overlay
    /// under the chat button, so the animation origin (top-right) matches the button location.
    /// </summary>
    public void ShowAnimated()
    {
        Visibility = Visibility.Visible;
        var scale = new DoubleAnimation
        {
            From = 0.6,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fade = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var transform = (ScaleTransform)PanelRoot.RenderTransform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        PanelRoot.BeginAnimation(OpacityProperty, fade);

        InputBox.Focus();
        Keyboard.Focus(InputBox);
    }

    public void HideAnimated()
    {
        var scale = new DoubleAnimation
        {
            From = 1.0,
            To = 0.6,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var fade = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => Visibility = Visibility.Collapsed;
        var transform = (ScaleTransform)PanelRoot.RenderTransform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        PanelRoot.BeginAnimation(OpacityProperty, fade);
    }

    private void ChatPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChatViewModel old)
        {
            old.Messages.CollectionChanged -= Messages_CollectionChanged;
        }
        if (e.NewValue is ChatViewModel vm)
        {
            vm.Messages.CollectionChanged += Messages_CollectionChanged;
        }
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep the newest message in view.
        Dispatcher.BeginInvoke(new Action(() => MessageScroll.ScrollToEnd()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter sends; Shift+Enter inserts a newline like most chat clients.
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
