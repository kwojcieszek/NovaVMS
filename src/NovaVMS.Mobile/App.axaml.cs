using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NovaVMS.Mobile.Services;
using NovaVMS.Mobile.Views;

namespace NovaVMS.Mobile;

public sealed partial class App : Application
{
    public static MobilePlatform? Platform { get; set; }

    public static MobileShell? Shell { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var platform = Platform ?? throw new InvalidOperationException("Mobile platform is not configured.");

        MobileLog.Install(platform.DataDirectory);

        if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () => Shell = new MobileShell(platform);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = Shell = new MobileShell(platform);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
