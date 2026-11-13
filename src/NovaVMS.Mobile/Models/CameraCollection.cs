using NovaVMS.Core.Configuration;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Models;

/// <summary>
/// Favorites, a saved view or a camera group. Holds only identifiers, so it can be resolved again
/// after a configuration from the settings database replaced every object.
/// </summary>
public sealed record CameraCollection
{
    public CameraCollectionKind Kind { get; init; }

    public Guid? Id { get; init; }

    public string Title { get; init; }

    public CameraCollection(CameraCollectionKind kind, Guid? id, string title)
    {
        Kind = kind;
        Id = id;
        Title = title;
    }

    public static CameraCollection Favorites(string title)
    {
        return new CameraCollection(CameraCollectionKind.Favorites, null, title);
    }

    public static CameraCollection ForView(SavedCameraView view)
    {
        return new CameraCollection(CameraCollectionKind.View, view.Id, view.Name);
    }

    public static CameraCollection ForGroup(CameraGroup group)
    {
        return new CameraCollection(CameraCollectionKind.Group, group.Id, group.Name);
    }

    /// <summary>Enabled cameras of the set in display order; missing cameras are skipped.</summary>
    public IReadOnlyList<MobileCamera> Resolve(AppConfiguration configuration)
    {
        IEnumerable<MobileCamera> cameras = Kind switch
        {
            CameraCollectionKind.Favorites => configuration.Devices
                .SelectMany(device => device.Channels.Where(channel => channel.IsFavorite).Select(channel => new MobileCamera(device, channel))),
            CameraCollectionKind.View => ResolveReferences(configuration,
                configuration.Views.FirstOrDefault(view => view.Id == Id)?.Layout.Cameras.OfType<CameraReference>() ?? []),
            _ => ResolveReferences(configuration, configuration.FindGroup(Id ?? Guid.Empty)?.Cameras ?? [])
        };

        return cameras.Where(camera => camera.Channel.Enabled).Distinct().ToList();
    }

    private static IEnumerable<MobileCamera> ResolveReferences(AppConfiguration configuration, IEnumerable<CameraReference> references)
    {
        foreach (var reference in references)
        {
            if (configuration.TryFindCamera(reference, out var device, out var channel))
            {
                yield return new MobileCamera(device, channel);
            }
        }
    }
}
