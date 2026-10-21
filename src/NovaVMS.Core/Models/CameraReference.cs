using System.Text.Json.Serialization;

namespace NovaVMS.Core.Models;

/// <summary>
/// Points to a camera: a channel of a specific device, identified by the channel's permanent <see cref="ChannelId"/>.
/// </summary>
public sealed record CameraReference
{
    public Guid DeviceId { get; init; }

    public Guid ChannelId { get; init; }

    /// <summary>
    /// Channel number of references written before cameras had identifiers. It is read only to convert such
    /// references (see <see cref="IsLegacy"/>) and is not written once a reference has an identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ChannelNumber { get; init; }

    [JsonIgnore]
    public bool IsLegacy => ChannelId == Guid.Empty;

    public static CameraReference For(Device device, Channel channel) => new() { DeviceId = device.Id, ChannelId = channel.Id };

    public bool Equals(CameraReference? other)
    {
        if (other is null || DeviceId != other.DeviceId)
        {
            return false;
        }

        return IsLegacy && other.IsLegacy ? ChannelNumber == other.ChannelNumber : ChannelId == other.ChannelId;
    }

    public override int GetHashCode() => IsLegacy ? HashCode.Combine(DeviceId, ChannelNumber) : HashCode.Combine(DeviceId, ChannelId);
}
