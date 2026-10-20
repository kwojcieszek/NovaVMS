using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.App.Views;

namespace NovaVMS.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            UnhandledExceptionLogger.AttachToDispatcher();
            global::LibVLCSharp.Shared.Core.Initialize();

            _services = ServiceConfiguration.Build();

            // Resolving these services subscribes them to configuration changes.
            _services.GetRequiredService<ThemeService>();
            _services.GetRequiredService<LanguageService>();
            _services.GetRequiredService<SdkLibraryService>();

            var viewModel = _services.GetRequiredService<MainWindowViewModel>();

            // Video overlays are separate windows; the application must end with the main window.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.Exit += OnDesktopExit;

            viewModel.InitializeCommand.Execute(null);
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
