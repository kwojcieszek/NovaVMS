using System.Globalization;
using Avalonia.Input;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Drag-and-drop payload carrying a list of cameras as text ("deviceId:channel;deviceId:channel").
/// </summary>
public static class CameraDragData
{
    private const char ItemSeparator = ';';
    private const char PartSeparator = ':';

    /// <summary>
    /// Application-specific format, so only NovaVMS windows accept the payload.
    /// </summary>
    public static DataFormat<string> Format { get; } = DataFormat.CreateStringApplicationFormat("NovaVMS.Cameras");

    public static IDataTransfer Create(IEnumerable<CameraReference> cameras)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Format, Encode(cameras)));
        return transfer;
    }

    public static bool Contains(IDataTransfer data)
    {
        return data.Contains(Format);
    }

    public static IReadOnlyList<CameraReference> Read(IDataTransfer data)
    {
        return Decode(data.TryGetValue(Format));
    }

    public static string Encode(IEnumerable<CameraReference> cameras)
    {
        return string.Join(ItemSeparator, cameras.Select(camera =>
            $"{camera.DeviceId:D}{PartSeparator}{camera.ChannelNumber.ToString(CultureInfo.InvariantCulture)}"));
    }

    public static IReadOnlyList<CameraReference> Decode(string? text)
    {
        var cameras = new List<CameraReference>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return cameras;
        }

        foreach (var item in text.Split(ItemSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split(PartSeparator);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!Guid.TryParse(parts[0], out var deviceId))
            {
                continue;
            }

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channelNumber))
            {
                continue;
            }

            cameras.Add(new CameraReference { DeviceId = deviceId, ChannelNumber = channelNumber });
        }

        return cameras;
    }
}
