using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Core.Configuration;

/// <summary>Settings export and import rules shared by the desktop and mobile applications.</summary>
public static class AppConfigurationTransfer
{
    /// <summary>
    /// Writes a copy of the configuration to a stream. Passwords are cleared unless explicitly requested.
    /// </summary>
    public static async Task ExportAsync(IConfigurationSerializer serializer, Stream stream, AppConfiguration current, bool includePasswords)
    {
        var copy = current.CreateSnapshot();

        if (!includePasswords)
        {
            RemoveSecrets(copy);
        }

        await Task.Run(() => serializer.SerializeAsync(stream, copy));
    }

    /// <summary>
    /// Reads an exported configuration from a stream.
    /// </summary>
    public static async Task<AppConfiguration> ReadAsync(IConfigurationSerializer serializer, Stream stream)
    {
        var configuration = await Task.Run(() => serializer.DeserializeAsync(stream));

        return AppConfigurationNormalizer.Normalize(configuration);
    }

    /// <summary>
    /// Prepares a configuration received from the settings database to replace the current one. The SDK folder stays
    /// a setting of the installation that set it, and a version without users keeps the users known here.
    /// </summary>
    public static AppConfiguration PrepareReplacement(AppConfiguration current, AppConfiguration received)
    {
        var replacement = AppConfigurationNormalizer.Normalize(received);

        replacement.Settings.HcNetSdkDirectory = current.Settings.HcNetSdkDirectory;

        if (replacement.Users.Count == 0)
        {
            replacement.Users = current.Users.Select(user => user.Copy()).ToList();
        }

        return replacement;
    }

    /// <summary>
    /// Clears device passwords, stream keys and user password hashes of an exported copy.
    /// </summary>
    public static AppConfiguration RemoveSecrets(AppConfiguration copy)
    {
        foreach (var device in copy.Devices)
        {
            device.Password = string.Empty;
            device.StreamEncryptionKey = string.Empty;
        }

        foreach (var user in copy.Users)
        {
            user.PasswordHash = string.Empty;
            user.PasswordSalt = string.Empty;
            user.PasswordIterations = 0;
        }

        return copy;
    }

    /// <summary>
    /// Merges an imported configuration: items with the same ID are replaced, new ones are added
    /// and general settings are overwritten. Empty imported passwords keep the current password.
    /// </summary>
    public static AppConfigurationImportResult Merge(AppConfiguration current, AppConfiguration imported)
    {
        var devicesAdded = 0;
        var devicesUpdated = 0;

        foreach (var device in imported.Devices)
        {
            var index = current.Devices.FindIndex(existing => existing.Id == device.Id);

            if (index < 0)
            {
                current.Devices.Add(device);

                devicesAdded++;
                continue;
            }

            if (string.IsNullOrEmpty(device.Password))
            {
                device.Password = current.Devices[index].Password;
            }

            if (string.IsNullOrEmpty(device.StreamEncryptionKey))
            {
                device.StreamEncryptionKey = current.Devices[index].StreamEncryptionKey;
            }

            current.Devices[index] = device;
            devicesUpdated++;
        }

        var groupsAdded = 0;
        var groupsUpdated = 0;

        foreach (var group in imported.Groups)
        {
            var index = current.Groups.FindIndex(existing => existing.Id == group.Id);

            if (index < 0)
            {
                current.Groups.Add(group);

                groupsAdded++;
            }
            else
            {
                current.Groups[index] = group;
                groupsUpdated++;
            }
        }

        current.Settings = imported.Settings;

        foreach (var view in imported.Views)
        {
            var index = current.Views.FindIndex(existing => existing.Id == view.Id);

            if (index < 0)
            {
                current.Views.Add(view.Copy());
            }
            else
            {
                current.Views[index] = view.Copy();
            }
        }

        MergeUsers(current, imported.Users);

        return new AppConfigurationImportResult(devicesAdded, devicesUpdated, groupsAdded, groupsUpdated);
    }

    /// <summary>
    /// Users with the same ID are replaced; an export without passwords keeps the current password.
    /// New users without a password, or with a name already taken, are skipped so nobody gets an account without a password.
    /// </summary>
    private static void MergeUsers(AppConfiguration current, IEnumerable<UserAccount> users)
    {
        foreach (var user in users)
        {
            var index = current.Users.FindIndex(existing => existing.Id == user.Id);
            var sameName = current.FindUser(user.UserName);

            if (index < 0)
            {
                if (user.HasPassword && sameName is null && !string.IsNullOrWhiteSpace(user.UserName))
                {
                    current.Users.Add(user.Copy());
                }

                continue;
            }

            if (sameName is not null && sameName.Id != user.Id)
            {
                continue;
            }

            var updated = user.Copy();

            if (!updated.HasPassword)
            {
                updated.PasswordHash = current.Users[index].PasswordHash;
                updated.PasswordSalt = current.Users[index].PasswordSalt;
                updated.PasswordIterations = current.Users[index].PasswordIterations;
            }

            current.Users[index] = updated;
        }
    }
}
