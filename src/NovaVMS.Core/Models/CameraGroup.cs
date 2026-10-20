using System.Text.Json.Serialization;

namespace NovaVMS.Core.Models;

/// <summary>
/// Group of cameras shown in the resource tree.
/// A group created from a device (e.g. all cameras of a recorder) keeps a link to it;
/// a custom group is defined freely by the user.
/// </summary>
public sealed class CameraGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Device the group was created from; new channels of that device are appended to the group.
    /// </summary>
    public Guid? SourceDeviceId { get; set; }

    public List<CameraReference> Cameras { get; set; } = new();

    [JsonIgnore]
    public bool IsCustom => SourceDeviceId is null;
}
