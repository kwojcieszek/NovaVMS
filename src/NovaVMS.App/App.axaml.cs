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
            global::LibVLCSharp.Shared.Core.Initialize();

            _services = ServiceConfiguration.Build();

            // Resolving these services subscribes them to configuration changes.
            _services.GetRequiredService<ThemeService>();
            _services.GetRequiredService<LanguageService>();
            _services.GetRequiredService<SdkLibraryService>();
            _services.GetRequiredService<DeviceConnectionCleanupService>();

            var viewModel = _services.GetRequiredService<MainWindowViewModel>();

            // Video overlays are separate windows; the application must end with the main window.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            if (_services.GetRequiredService<LocalSettingsService>().Current.StartFullScreen)
            {
                desktop.MainWindow.WindowState = WindowState.FullScreen;
            }
            desktop.Exit += OnDesktopExit;
            SingleInstance.Current?.ListenForActivation(() => Dispatcher.UIThread.Post(() =>
            {
                if (desktop.MainWindow is not { } window) return;
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            }));
            // The main window starts the initialization once it is shown: the login window needs it as its owner.
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        UnhandledExceptionLogger.IsShuttingDown = true;
        _services?.Dispose();
        _services = null;
    }
}
