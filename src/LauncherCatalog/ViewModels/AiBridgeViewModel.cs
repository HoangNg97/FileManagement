using System;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using LauncherCatalog.Services;
using LauncherCatalog.Services.Ai;
using Clipboard = System.Windows.Clipboard;

namespace LauncherCatalog.ViewModels;

/// <summary>
/// Backs the "AI Bridge" settings tab. Surfaces the shared <see cref="AiBridgeManager"/> state
/// (enabled, port, token, status) for binding and exposes commands to copy/regenerate the token,
/// apply a new port, and (placeholder) sign in to Gemini. All mutating actions are delegated to the
/// manager so there is exactly one listener for the whole app.
/// </summary>
public sealed class AiBridgeViewModel : ObservableObject
{
    private readonly AiBridgeManager _manager = AiBridgeManager.Current;
    private readonly LocalizationService _localizer = LocalizationService.Current;
    private readonly Dispatcher _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    private string _portText;
    private bool _isTokenVisible;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public AiBridgeViewModel()
    {
        _portText = _manager.Port.ToString(CultureInfo.InvariantCulture);

        ToggleTokenVisibilityCommand = new RelayCommand(() => IsTokenVisible = !IsTokenVisible);
        CopyTokenCommand = new RelayCommand(CopyToken);
        CopyEndpointCommand = new RelayCommand(CopyEndpoint);
        RegenerateTokenCommand = new AsyncRelayCommand(RegenerateTokenAsync);
        ApplyPortCommand = new AsyncRelayCommand(ApplyPortAsync, () => !IsBusy);
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy);

        _manager.StateChanged += OnManagerStateChanged;
        _localizer.CultureChanged += OnCultureChanged;
    }

    public RelayCommand ToggleTokenVisibilityCommand { get; }
    public RelayCommand CopyTokenCommand { get; }
    public RelayCommand CopyEndpointCommand { get; }
    public AsyncRelayCommand RegenerateTokenCommand { get; }
    public AsyncRelayCommand ApplyPortCommand { get; }
    public AsyncRelayCommand LoginCommand { get; }

    public bool IsEnabled
    {
        get => _manager.IsEnabled;
        set
        {
            if (_manager.IsEnabled == value)
            {
                return;
            }

            _ = SetEnabledAsync(value);
        }
    }

    public string PortText
    {
        get => _portText;
        set => SetProperty(ref _portText, value);
    }

    public string EndpointUrl => _manager.EndpointUrl;

    public string Token => _manager.Token;

    public bool IsTokenVisible
    {
        get => _isTokenVisible;
        set
        {
            if (SetProperty(ref _isTokenVisible, value))
            {
                OnPropertyChanged(nameof(DisplayToken));
                OnPropertyChanged(nameof(ToggleTokenVisibilityLabel));
            }
        }
    }

    public string DisplayToken => IsTokenVisible || string.IsNullOrEmpty(Token)
        ? Token
        : new string('\u2022', 32);

    public string ToggleTokenVisibilityLabel => _localizer.Get(
        IsTokenVisible ? "AiBridge.Token.Hide" : "AiBridge.Token.Show");

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ApplyPortCommand.RaiseCanExecuteChanged();
                LoginCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string StatusText => _manager.Status switch
    {
        AiBridgeStatus.Running => string.Format(
            CultureInfo.CurrentCulture, _localizer.Get("AiBridge.Status.Running"), _manager.Port),
        AiBridgeStatus.PortInUse => string.Format(
            CultureInfo.CurrentCulture, _localizer.Get("AiBridge.Status.PortInUse"), _manager.Port),
        AiBridgeStatus.Error => string.Format(
            CultureInfo.CurrentCulture, _localizer.Get("AiBridge.Status.Error"), _manager.LastError ?? string.Empty),
        _ => _localizer.Get("AiBridge.Status.Disabled")
    };

    public bool IsRunning => _manager.Status == AiBridgeStatus.Running;

    public bool IsSignedIn => _manager.IsSignedIn;

    public string SignInStatusText => _localizer.Get(
        _manager.IsSignedIn ? "AiBridge.Gemini.SignedIn" : "AiBridge.Gemini.SignedOut");

    public string LoginButtonText => _localizer.Get(
        _manager.IsSignedIn ? "AiBridge.Gemini.Relogin" : "AiBridge.Gemini.Login");

    private async System.Threading.Tasks.Task SetEnabledAsync(bool enabled)
    {
        IsBusy = true;
        try
        {
            await _manager.SetEnabledAsync(enabled);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async System.Threading.Tasks.Task ApplyPortAsync()
    {
        if (!int.TryParse(PortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            || port is <= 0 or > 65535)
        {
            StatusMessage = _localizer.Get("AiBridge.Message.InvalidPort");
            PortText = _manager.Port.ToString(CultureInfo.InvariantCulture);
            return;
        }

        if (port == _manager.Port)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _manager.SetPortAsync(port);
            StatusMessage = _localizer.Get("AiBridge.Message.PortApplied");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async System.Threading.Tasks.Task RegenerateTokenAsync()
    {
        await _manager.RegenerateTokenAsync();
        StatusMessage = _localizer.Get("AiBridge.Message.TokenRegenerated");
    }

    private async System.Threading.Tasks.Task LoginAsync()
    {
        IsBusy = true;
        StatusMessage = _localizer.Get("AiBridge.Message.LoginInProgress");
        try
        {
            await _manager.LoginAsync();
            StatusMessage = _localizer.Get(
                _manager.IsSignedIn ? "AiBridge.Message.LoginSucceeded" : "AiBridge.Message.LoginNotDetected");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CopyToken()
    {
        if (string.IsNullOrEmpty(Token))
        {
            return;
        }

        try
        {
            Clipboard.SetText(Token);
            StatusMessage = _localizer.Get("AiBridge.Message.TokenCopied");
        }
        catch (Exception)
        {
            StatusMessage = _localizer.Get("AiBridge.Message.CopyFailed");
        }
    }

    private void CopyEndpoint()
    {
        try
        {
            Clipboard.SetText(EndpointUrl);
            StatusMessage = _localizer.Get("AiBridge.Message.EndpointCopied");
        }
        catch (Exception)
        {
            StatusMessage = _localizer.Get("AiBridge.Message.CopyFailed");
        }
    }

    private void OnManagerStateChanged(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _portText = _manager.Port.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(PortText));
            OnPropertyChanged(nameof(EndpointUrl));
            OnPropertyChanged(nameof(Token));
            OnPropertyChanged(nameof(DisplayToken));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(SignInStatusText));
            OnPropertyChanged(nameof(LoginButtonText));
        });
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleTokenVisibilityLabel));
        OnPropertyChanged(nameof(SignInStatusText));
        OnPropertyChanged(nameof(LoginButtonText));
    }
}
