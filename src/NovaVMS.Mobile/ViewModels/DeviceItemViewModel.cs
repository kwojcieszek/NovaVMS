using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Configuration;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Services;
using NovaVMS.Presentation.Localization;
using NovaVMS.Presentation.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Device card of the device list.</summary>
public sealed partial class DeviceItemViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;
    private readonly Device _device;

    public string Name => _device.DisplayName;

    public string Details => Localizer.Format("Mobile_DeviceDetails", _device.Host, ProtocolName(_device.Protocol), _device.Channels.Count);

    public string Hardware => string.Join(" · ", new[] { _device.Model, _device.SerialNumber, _device.FirmwareVersion }.Where(text => !string.IsNullOrWhiteSpace(text)));

    public bool HasHardware => Hardware.Length > 0;

    public DeviceItemViewModel(ShellViewModel shell, Device device)
    {
        _shell = shell;
        _device = device;
    }

    private static string ProtocolName(DeviceProtocol protocol)
    {
        return ProtocolOption.Find(protocol).Label;
    }

    /// <summary>
    /// Keeps the identity and favorite flag of channels that already exist, so groups and views keep their cameras.
    /// </summary>
    private static List<Channel> MergeChannels(IEnumerable<Channel> existing, IEnumerable<Channel> downloaded)
    {
        var known = existing.ToList();
        var channels = downloaded.ToList();

        foreach (var channel in channels)
        {
            if (known.FirstOrDefault(c => c.Number == channel.Number) is { } old)
            {
                channel.Id = old.Id;
                channel.IsFavorite = old.IsFavorite;
                channel.Enabled = old.Enabled;
            }
        }

        return channels;
    }

    [RelayCommand]
    private Task DownloadChannelsAsync()
    {
        return _shell.RunAsync(DownloadAsync);
    }

    [RelayCommand]
    private void Edit()
    {
        _shell.EditDevice(_device);
    }

    private async Task DownloadAsync(CancellationToken token)
    {
        var resolved = MobileDevices.Resolve(_device, _shell.LocalSettings.UseOptionalProtocol);

        using var client = await MobileDevices.ConnectAsync(_device, _shell.LocalSettings.UseOptionalProtocol, token);

        var downloaded = await client.GetChannelsAsync(token);
        var updated = _device.ForHost(_device.Host);
        var usesOptional = resolved is not null && resolved.Protocol != _device.Protocol && _device.OptionalConnection is not null;

        updated.OptionalConnection = _device.OptionalConnection?.Copy();

        if (usesOptional)
        {
            updated.OptionalConnection!.Channels = MergeChannels(_device.OptionalConnection!.Channels, downloaded);

            // A device whose primary protocol is not available on phones still needs cameras for groups and views.
            if (updated.Channels.Count == 0)
            {
                updated.Channels = updated.OptionalConnection.Channels
                    .Select(channel => new Channel { Number = channel.Number, Name = channel.Name, Enabled = channel.Enabled })
                    .ToList();
            }
        }
        else
        {
            updated.Channels = MergeChannels(_device.Channels, downloaded);
        }

        _shell.Current.SyncDeviceGroup(updated, allowCreate: true);

        await _shell.ReplaceDeviceAsync(_device, updated, token);

        _shell.Status = Localizer.Format("Mobile_ChannelsDownloaded", downloaded.Count);
        _shell.ShowDevices();
    }
}
