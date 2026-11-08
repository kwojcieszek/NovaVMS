using Avalonia;
using Avalonia.Headless;
using NovaVMS.Mobile.Services;

[assembly: AvaloniaTestApplication(typeof(NovaVMS.Mobile.Tests.HeadlessApplication))]

namespace NovaVMS.Mobile.Tests;

public static class HeadlessApplication
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.Platform = new MobilePlatform(Path.GetTempPath(), new TestProtection(), () => throw new NotSupportedException());

        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
