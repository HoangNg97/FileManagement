using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LauncherCatalog.Services;

namespace LauncherCatalog.Services.Ai;

/// <summary>Coarse state of the AI Bridge for display in the settings tab.</summary>
public enum AiBridgeStatus
{
    /// <summary>Feature toggled off; no listener.</summary>
    Disabled,

    /// <summary>Enabled and listening on the loopback port.</summary>
    Running,

    /// <summary>Enabled but the configured port is taken by another process.</summary>
    PortInUse,

    /// <summary>Enabled but the listener failed to start for another reason.</summary>
    Error
}

/// <summary>
/// Owns the single <see cref="AiBridgeServer"/> instance and its persisted settings
/// (<c>ai_bridge_enabled</c>, <c>ai_bridge_port</c>, <c>ai_bridge_token</c>). A process-wide
/// singleton (<see cref="Current"/>) so the app shell and the AI Bridge tab share one server:
/// the shell drives <see cref="InitializeAsync"/>/<see cref="ShutdownAsync"/>, the tab toggles
/// enable, edits the port, and copies/regenerates the token. The bearer token is stored
/// DPAPI-encrypted (see <see cref="DpapiProtector"/>).
/// </summary>
public sealed class AiBridgeManager
{
    public static AiBridgeManager Current { get; } = new();

    private const string EnabledKey = "ai_bridge_enabled";
    private const string PortKey = "ai_bridge_port";
    private const string TokenKey = "ai_bridge_token";

    private readonly EntryRepository _settings = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CopilotBackend _backend = new();
    private AiBridgeServer? _server;
    private bool _initialized;
    private string _token = string.Empty;

    private AiBridgeManager()
    {
    }

    /// <summary>Raised after any state change (status, port, token, enabled) on a worker thread.</summary>
    public event EventHandler? StateChanged;

    public bool IsEnabled { get; private set; } = true;
    public int Port { get; private set; } = AiBridgeServer.DefaultPort;
    public string Token => _token;
    public AiBridgeStatus Status { get; private set; } = AiBridgeStatus.Disabled;
    public string? LastError { get; private set; }

    /// <summary>Whether a GitHub account is signed in for Copilot.</summary>
    public bool IsSignedIn => _backend.IsReady;

    /// <summary>GitHub login of the signed-in account, if any.</summary>
    public string? SignedInLogin => _backend.Login;

    /// <summary>The loopback URL the extension targets.</summary>
    public string EndpointUrl => $"http://127.0.0.1:{Port}";

    /// <summary>Loads persisted settings and starts the server when the feature is enabled.</summary>
    public async Task InitializeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            await LoadSettingsAsync().ConfigureAwait(false);
            await _backend.LoadAsync().ConfigureAwait(false);

            if (IsEnabled)
            {
                await StartServerAsync().ConfigureAwait(false);
            }
            else
            {
                Status = AiBridgeStatus.Disabled;
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseStateChanged();
    }

    /// <summary>Turns the feature on or off, starting/stopping the listener and persisting the choice.</summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsEnabled == enabled && _initialized)
            {
                return;
            }

            IsEnabled = enabled;
            await _settings.SetSettingValueAsync(EnabledKey, enabled ? "true" : "false").ConfigureAwait(false);

            if (enabled)
            {
                await StartServerAsync().ConfigureAwait(false);
            }
            else
            {
                await StopServerAsync().ConfigureAwait(false);
                Status = AiBridgeStatus.Disabled;
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseStateChanged();
    }

    /// <summary>Persists a new port and restarts the listener on it when enabled.</summary>
    public async Task SetPortAsync(int port)
    {
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Port == port)
            {
                return;
            }

            Port = port;
            await _settings.SetSettingValueAsync(PortKey, port.ToString()).ConfigureAwait(false);

            if (IsEnabled)
            {
                await StopServerAsync().ConfigureAwait(false);
                await StartServerAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseStateChanged();
    }

    /// <summary>Generates a fresh bearer token, persists it (encrypted), and invalidates the old one immediately.</summary>
    public async Task RegenerateTokenAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _token = AiBridgeServer.GenerateToken();
            await _settings.SetSettingValueAsync(TokenKey, DpapiProtector.Protect(_token)).ConfigureAwait(false);
            _server?.UpdateToken(_token);
        }
        finally
        {
            _gate.Release();
        }

        RaiseStateChanged();
    }

    /// <summary>Signs in to GitHub Copilot on the web (device flow); <paramref name="showCode"/> receives the code to enter.</summary>
    public async Task SignInAsync(Action<string> showCode, CancellationToken cancellationToken)
    {
        await _backend.SignInAsync(showCode, cancellationToken).ConfigureAwait(false);
        RaiseStateChanged();
    }

    public async Task SignOutAsync()
    {
        await _backend.SignOutAsync().ConfigureAwait(false);
        RaiseStateChanged();
    }

    /// <summary>Stops the listener on app shutdown.</summary>
    public async Task ShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopServerAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await _backend.DisposeAsync().ConfigureAwait(false);
    }

    private async Task LoadSettingsAsync()
    {
        var enabledValue = await _settings.GetSettingValueAsync(EnabledKey).ConfigureAwait(false);
        IsEnabled = enabledValue is null || !string.Equals(enabledValue, "false", StringComparison.OrdinalIgnoreCase);

        var portValue = await _settings.GetSettingValueAsync(PortKey).ConfigureAwait(false);
        Port = int.TryParse(portValue, out var parsedPort) && parsedPort is > 0 and <= 65535
            ? parsedPort
            : AiBridgeServer.DefaultPort;

        var storedToken = await _settings.GetSettingValueAsync(TokenKey).ConfigureAwait(false);
        if (DpapiProtector.TryUnprotect(storedToken, out var plaintext, out var wasEncrypted) && plaintext.Length > 0)
        {
            _token = plaintext;
            if (!wasEncrypted)
            {
                // Migrate a legacy Stage 1 plaintext token to encrypted storage.
                await _settings.SetSettingValueAsync(TokenKey, DpapiProtector.Protect(_token)).ConfigureAwait(false);
            }
        }
        else
        {
            _token = AiBridgeServer.GenerateToken();
            await _settings.SetSettingValueAsync(TokenKey, DpapiProtector.Protect(_token)).ConfigureAwait(false);
        }
    }

    private async Task StartServerAsync()
    {
        await StopServerAsync().ConfigureAwait(false);

        if (!IsPortAvailable(Port))
        {
            Status = AiBridgeStatus.PortInUse;
            LastError = null;
            AppLogger.WriteInfo($"AI bridge port {Port} is already in use; bridge not started.");
            return;
        }

        try
        {
            var server = new AiBridgeServer(_token, _backend, Port);
            await server.StartAsync().ConfigureAwait(false);
            _server = server;
            Status = AiBridgeStatus.Running;
            LastError = null;
            AppLogger.WriteInfo($"AI bridge listening on {EndpointUrl}.");
        }
        catch (Exception exception)
        {
            Status = AiBridgeStatus.Error;
            LastError = exception.Message;
            AppLogger.WriteInfo($"AI bridge failed to start: {exception.Message}");

            if (_server is not null)
            {
                await _server.DisposeAsync().ConfigureAwait(false);
                _server = null;
            }
        }
    }

    private async Task StopServerAsync()
    {
        if (_server is null)
        {
            return;
        }

        await _server.DisposeAsync().ConfigureAwait(false);
        _server = null;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
