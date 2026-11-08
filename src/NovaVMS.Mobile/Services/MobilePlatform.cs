namespace NovaVMS.Mobile.Services;

/// <summary>Platform services supplied by the Android and iOS heads.</summary>
/// <param name="Display">Full screen and orientation control; null keeps the screen as it is.</param>
public sealed record MobilePlatform(string DataDirectory, IDataProtection Protection, Func<IMobilePlayer> CreatePlayer, IDeviceDisplay? Display = null);
