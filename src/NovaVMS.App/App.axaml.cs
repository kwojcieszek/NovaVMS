using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.App.Views;
using NovaVMS.Core.Abstractions;

namespace NovaVMS.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private Task? _serviceDisposal;

    internal Task DisposeServicesAsync() => _serviceDisposal ??= DisposeServicesCoreAsync();

    private async Task DisposeServicesCoreAsync()
    {
        var services = _services;
        _services = null;
        if (services is null) return;
        var factory = services.GetService<IDeviceClientFactory>();
        var libVlc = services.GetService<LibVLCSharp.Shared.LibVLC>();
        // Native resources are idempotently released in the background. Dispose the
        // provider on the UI thread afterwards: transient settings pages own UI timers.
        await Task.Run(() =>
        {
            (factory as IDisposable)?.Dispose();
            libVlc?.Dispose();
        });
        services.Dispose();
        await UnhandledExceptionLogger.FlushAsync();
    }

    public override void Initialize()
    {
        UnhandledExceptionLogger.AttachToDispatcher();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The start window is drawn before LibVLC and the services load, and stays until the configuration
            // and settings database are ready (or the login window opens).
            var splash = new BusyWindow("Startup_Title", "Startup_Message");
            splash.Show();
            Dispatcher.UIThread.Post(() => Start(desktop, splash), DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Start(IClassicDesktopStyleApplicationLifetime desktop, BusyWindow splash)
    {
        try
        {
            global::LibVLCSharp.Shared.Core.Initialize();

            _services = ServiceConfiguration.Build();

            // Resolving these services subscribes them to configuration changes.
            _services.GetRequiredService<ThemeService>();
            _services.GetRequiredService<LanguageService>();
            _services.GetRequiredService<SdkLibraryService>();
            _services.GetRequiredService<DeviceConnectionCleanupService>();

            var viewModel = _services.GetRequiredService<MainWindowViewModel>();
            viewModel.StartupFinished += (_, _) => splash.Finish();

            // Video overlays are separate windows; the application must end with the main window.
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            if (_services.GetRequiredService<LocalSettingsService>().Current.StartFullScreen)
            {
                mainWindow.WindowState = WindowState.FullScreen;
            }
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += OnDesktopExit;
            SingleInstance.Current?.ListenForActivation(() => Dispatcher.UIThread.Post(() =>
            {
                if (desktop.MainWindow is not { } window) return;
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            }));
            // The main window starts the initialization once it is shown: the login window needs it as its owner.
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            UnhandledExceptionLogger.LogStartupFailure(exception);
            splash.Finish();
            desktop.Shutdown(1);
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        UnhandledExceptionLogger.IsShuttingDown = true;
        _services?.Dispose();
        _services = null;
    }
}
