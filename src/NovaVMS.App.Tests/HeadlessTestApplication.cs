using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(NovaVMS.App.Tests.HeadlessTestApplication))]

namespace NovaVMS.App.Tests;

public sealed class HeadlessTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HeadlessTestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
