using NovaVMS.Core.Models;
using NovaVMS.Core.Security;

namespace NovaVMS.Core.Configuration;

/// <summary>
/// Rules every application applies to a loaded or received configuration, so desktop and mobile
/// installations sharing a settings database keep identical data.
/// </summary>
public static class AppConfigurationNormalizer
{
    /// <summary>
    /// Name and password of the account created when the configuration has no users.
    /// </summary>
    public const string DefaultUserName = "admin";

    /// <summary>
    /// Adds the default administrator when no user exists, so the application can always be logged into.
    /// </summary>
    public static bool EnsureDefaultUser(AppConfiguration configuration)
    {
        if (configuration.Users.Count > 0)
        {
            return false;
        }

        var admin = new UserAccount { UserName = DefaultUserName, Permissions = UserPermissions.All };

        PasswordHasher.SetPassword(admin, DefaultUserName);
        configuration.Users.Add(admin);

        return true;
    }

    /// <summary>
    /// Replaces missing collections (JSON files edited by hand may contain nulls), normalizes channel names
    /// and gives every camera a stable identity.
    /// </summary>
    public static AppConfiguration Normalize(AppConfiguration configuration)
    {
        configuration.Devices ??= new List<Device>();
        configuration.Groups ??= new List<CameraGroup>();
        configuration.Settings ??= new AppSettings();
        configuration.GroupLiveViews ??= new();
        configuration.Views ??= new();
        configuration.Users ??= new();

        configuration.Users.RemoveAll(user => user is null);

        foreach (var user in configuration.Users)
        {
            user.UserName ??= string.Empty;
            user.PasswordHash ??= string.Empty;
            user.PasswordSalt ??= string.Empty;
        }

        foreach (var view in configuration.Views)
        {
            view.Layout ??= new();
            view.Layout.Cameras ??= new();
            view.Layout.EmptySlots ??= new();
        }

        foreach (var device in configuration.Devices)
        {
            device.SecondaryHost ??= string.Empty;
            device.StreamEncryptionKey ??= string.Empty;

            if (device.OptionalConnection is { } optional)
            {
                optional.Host ??= string.Empty;
                optional.SecondaryHost ??= string.Empty;
                optional.Channels ??= new();
            }

            device.Channels ??= new List<Channel>();

            foreach (var channel in device.Channels)
            {
                // Older configurations may still hold stream numbers ("101") as channel names.
                channel.Name = ChannelNameNormalizer.Normalize(channel.Name ?? string.Empty, channel.Number);
            }
        }

        foreach (var group in configuration.Groups)
        {
            group.Cameras ??= new List<CameraReference>();
        }

        AssignCameraIdentities(configuration);

        return configuration;
    }

    /// <summary>
    /// Gives every channel a unique identity and converts references written with channel numbers to identities.
    /// Afterwards views, groups and remembered layouts no longer depend on how a device numbers its channels.
    /// </summary>
    private static void AssignCameraIdentities(AppConfiguration configuration)
    {
        var used = new HashSet<Guid>();

        foreach (var channel in configuration.Devices.SelectMany(device => device.Channels))
        {
            if (channel.Id == Guid.Empty || !used.Add(channel.Id))
            {
                channel.Id = Guid.NewGuid();

                used.Add(channel.Id);
            }
        }

        var devices = configuration.Devices.GroupBy(device => device.Id).ToDictionary(group => group.Key, group => group.First());

        CameraReference? Convert(CameraReference? reference)
        {
            if (reference is not { IsLegacy: true } || !devices.TryGetValue(reference.DeviceId, out var device))
            {
                return reference;
            }

            var channel = device.Channels.FirstOrDefault(candidate => candidate.Number == reference.ChannelNumber);

            return channel is null ? reference : CameraReference.For(device, channel);
        }

        foreach (var group in configuration.Groups)
        {
            group.Cameras = group.Cameras.Select(camera => Convert(camera)!).Distinct().ToList();
        }

        foreach (var layout in configuration.Views.Select(view => view.Layout).Concat(configuration.GroupLiveViews.Values.OfType<GroupLiveView>()))
        {
            layout.Cameras ??= new();
            layout.Cameras = layout.Cameras.Select(Convert).ToList();
        }
    }
}
