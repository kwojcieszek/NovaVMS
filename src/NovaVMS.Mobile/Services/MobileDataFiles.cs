using System.Text.Json;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Data files of the phone. They have the same names and formats as the desktop files
/// (<see cref="AppDataPaths"/>): config.json, sync.json and local.json, each encrypted for this device
/// where the desktop encrypts it for the computer account.
/// </summary>
public sealed class MobileDataFiles
{
    // Names used by earlier mobile versions.
    private const string LegacyConfigurationFileName = "config.enc";
    private const string LegacySyncOptionsFileName = "sync.enc";
    private const string LegacyLocalSettingsFileName = "settings.json";

    private readonly string _directory;

    public string ConfigurationFile => Path.Combine(_directory, AppDataPaths.ConfigurationFileName);

    public string SyncOptionsFile => Path.Combine(_directory, AppDataPaths.SyncOptionsFileName);

    public string LocalSettingsFile => Path.Combine(_directory, AppDataPaths.LocalSettingsFileName);

    public MobileDataFiles(string directory)
    {
        _directory = directory;
    }

    /// <summary>Renames files of earlier mobile versions to the shared names; existing shared files win.</summary>
    public void MigrateLegacyFiles()
    {
        Rename(LegacyConfigurationFileName, ConfigurationFile);
        Rename(LegacySyncOptionsFileName, SyncOptionsFile);
        MigrateLocalSettings();
    }

    private void Rename(string legacyName, string target)
    {
        var legacy = Path.Combine(_directory, legacyName);

        if (File.Exists(legacy) && !File.Exists(target))
        {
            File.Move(legacy, target);
        }
    }

    // Earlier mobile versions kept only the optional protocol choice, in the same property name as LocalSettings.
    private void MigrateLocalSettings()
    {
        var legacy = Path.Combine(_directory, LegacyLocalSettingsFileName);

        if (!File.Exists(legacy))
        {
            return;
        }

        if (!File.Exists(LocalSettingsFile))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(legacy));

                var settings = new LocalSettings
                {
                    UseOptionalProtocol = document.RootElement.TryGetProperty(nameof(LocalSettings.UseOptionalProtocol), out var value)
                        && value.ValueKind == JsonValueKind.True
                };

                File.WriteAllText(LocalSettingsFile, JsonSerializer.Serialize(settings));
            }
            catch (JsonException)
            {
                // A damaged file of an earlier version only held one preference; defaults are used.
            }
        }

        File.Delete(legacy);
    }
}
