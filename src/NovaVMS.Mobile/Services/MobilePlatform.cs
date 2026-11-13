namespace NovaVMS.Mobile.Services;

/// <summary>Platform services supplied by the Android and iOS heads.</summary>
public sealed record MobilePlatform
{
    public string DataDirectory { get; init; }

    public IDataProtection Protection { get; init; }

    public Func<IMobilePlayer> CreatePlayer { get; init; }

    /// <summary>Full screen and orientation control; null keeps the screen as it is.</summary>
    public IDeviceDisplay? Display { get; init; }

    public MobilePlatform(string dataDirectory, IDataProtection protection, Func<IMobilePlayer> createPlayer, IDeviceDisplay? display = null)
    {
        DataDirectory = dataDirectory;
        Protection = protection;
        CreatePlayer = createPlayer;
        Display = display;
    }
}
