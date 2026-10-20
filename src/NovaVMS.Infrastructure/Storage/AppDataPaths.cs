namespace NovaVMS.Infrastructure.Storage;

/// <summary>
/// Locations of application data in the user's profile:
/// Windows - %APPDATA%\NovaVMS, macOS - ~/Library/Application Support/NovaVMS, Linux - ~/.config/NovaVMS.
/// The NOVAVMS_DATA_DIR environment variable overrides the location (portable use, testing).
/// </summary>
public static class AppDataPaths
{
    private const string ApplicationFolderName = "NovaVMS";
    private const string DataDirectoryVariable = "NOVAVMS_DATA_DIR";

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string ConfigurationFile { get; } = Path.Combine(DataDirectory, "config.json");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        return Path.Combine(GetBaseDirectory(), ApplicationFolderName);
    }

    private static string GetBaseDirectory()
    {
        // On macOS .NET maps LocalApplicationData to ~/Library/Application Support, the standard place for app data.
        var folder = OperatingSystem.IsMacOS()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.ApplicationData;

        return Environment.GetFolderPath(folder);
    }
}
