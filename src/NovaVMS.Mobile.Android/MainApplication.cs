using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using NovaVMS.Mobile.Native;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.Android;

[Application]
public sealed class MainApplication : AvaloniaAndroidApplication<App>
{
    public MainApplication(nint handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.Platform = new MobilePlatform(FilesDir!.AbsolutePath, new AndroidProtection(), () => new SystemVideoPlayer(), AndroidDeviceDisplay.Instance);

        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
