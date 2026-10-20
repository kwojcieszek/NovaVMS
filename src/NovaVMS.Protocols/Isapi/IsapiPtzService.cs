using System.Net;
using System.Xml.Linq;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// PTZ control over ISAPI. Recorders use the channel proxy path,
/// falling back to the direct PTZCtrl path when the proxy is not available.
/// </summary>
internal sealed class IsapiPtzService
{
    private const int MaxValue = 100;

    private readonly IIsapiTransport _transport;
    private readonly Device _device;

    public IsapiPtzService(IIsapiTransport transport, Device device)
    {
        _transport = transport;
        _device = device;
    }

    public Task MoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken)
    {
        var ns = IsapiNamespace.Value;
        var body = new XElement(ns + "PTZData",
            new XAttribute("version", IsapiNamespace.Version),
            new XElement(ns + "pan", Math.Clamp(pan, -MaxValue, MaxValue)),
            new XElement(ns + "tilt", Math.Clamp(tilt, -MaxValue, MaxValue)),
            new XElement(ns + "zoom", Math.Clamp(zoom, -MaxValue, MaxValue)));

        return ExecuteWithFallbackAsync(channelNumber, basePath => _transport.PutAsync($"{basePath}/continuous", body, cancellationToken));
    }

    public Task StopAsync(int channelNumber, CancellationToken cancellationToken)
    {
        return MoveAsync(channelNumber, 0, 0, 0, cancellationToken);
    }

    public async Task<IReadOnlyList<PtzPreset>> GetPresetsAsync(int channelNumber, CancellationToken cancellationToken)
    {
        XDocument? document = null;
        await ExecuteWithFallbackAsync(channelNumber, async basePath =>
        {
            document = await _transport.GetXmlAsync($"{basePath}/presets", cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var presets = new List<PtzPreset>();
        if (document is null)
        {
            return presets;
        }

        foreach (var element in document.DescendantsByLocalName("PTZPreset"))
        {
            if (!int.TryParse(element.ChildValue("id"), out var id))
            {
                continue;
            }

            if (string.Equals(element.ChildValue("enabled"), "false", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            presets.Add(new PtzPreset
            {
                Id = id,
                Token = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = element.ChildValue("presetName")
            });
        }

        return presets.OrderBy(preset => preset.Id).ToList();
    }

    public Task GotoPresetAsync(int channelNumber, int presetId, CancellationToken cancellationToken)
    {
        return ExecuteWithFallbackAsync(channelNumber, basePath => _transport.PutAsync($"{basePath}/presets/{presetId}/goto", null, cancellationToken));
    }

    private async Task ExecuteWithFallbackAsync(int channelNumber, Func<string, Task> operation)
    {
        var directPath = $"ISAPI/PTZCtrl/channels/{channelNumber}";
        if (_device.Type is not (DeviceType.Nvr or DeviceType.Dvr))
        {
            await operation(directPath).ConfigureAwait(false);
            return;
        }

        try
        {
            await operation($"ISAPI/ContentMgmt/PTZCtrlProxy/channels/{channelNumber}").ConfigureAwait(false);
        }
        catch (DeviceCommunicationException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await operation(directPath).ConfigureAwait(false);
        }
    }
}
