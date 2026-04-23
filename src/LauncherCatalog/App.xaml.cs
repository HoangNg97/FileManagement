using System.Windows;
using System.Windows.Threading;
using LauncherCatalog.Services;

namespace LauncherCatalog;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\LauncherCatalog.SingleInstance";
    private const string ActivationEventName = @"Local\LauncherCatalog.Activate";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationListenerCts;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterCrashHandlers();
        AppLogger.WriteInfo("Application starting.");

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            AppLogger.WriteInfo("Another instance is already running. Activating it and exiting.");
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        StartActivationListener();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopActivationListener();

        if (_ownsSingleInstanceMutex)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex may already be released during abnormal shutdown.
            }
        }

        _activationEvent?.Dispose();
        _singleInstanceMutex?.Dispose();

        AppLogger.WriteInfo($"Application exiting with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // An older build may own the mutex but not expose the activation event.
        }
        catch (UnauthorizedAccessException)
        {
            // If Windows denies the signal, simply avoid starting a duplicate instance.
        }
    }

    private void StartActivationListener()
    {
        if (_activationEvent is null)
        {
            return;
        }

        _activationListenerCts = new CancellationTokenSource();
        var cancellationToken = _activationListenerCts.Token;
        var activationEvent = _activationEvent;

        _ = Task.Run(() =>
        {
            var waitHandles = new[] { activationEvent, cancellationToken.WaitHandle };

            while (WaitHandle.WaitAny(waitHandles) == 0)
            {
                Dispatcher.BeginInvoke(ActivateMainWindow);
            }
        }, cancellationToken);
    }

    private void StopActivationListener()
    {
        if (_activationListenerCts is null)
        {
            return;
        }

        _activationListenerCts.Cancel();
        _activationEvent?.Set();
        _activationListenerCts.Dispose();
        _activationListenerCts = null;
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.ActivateFromExternalInstance();
        }
    }

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = AppLogger.WriteCrash(e.Exception, "WPF dispatcher");
        e.Handled = true;
        ShowCrashMessage(logPath);
        Shutdown(1);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLogger.WriteCrash(exception, "AppDomain", e.IsTerminating);
        }
        else
        {
            AppLogger.WriteCrash(
                new InvalidOperationException($"Unhandled non-exception object: {e.ExceptionObject}"),
                "AppDomain",
                e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.WriteCrash(e.Exception, "Unobserved task", isTerminating: false);
        e.SetObserved();
    }

    private static void ShowCrashMessage(string logPath)
    {
        try
        {
            System.Windows.MessageBox.Show(
                $"Launcher Catalog hit an unexpected error and saved a crash log here:\n\n{logPath}\n\nPlease send this file for debugging.",
                "Launcher Catalog crashed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If the UI subsystem is already failing, the file log is still the important part.
        }
    }
}
