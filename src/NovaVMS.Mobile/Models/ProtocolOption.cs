using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.Mobile.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Models;

/// <summary>
/// Entry of the protocol list in the device form.
/// </summary>
public sealed class ProtocolOption : ObservableObject
{
    private readonly string _labelKey;

    public static IReadOnlyList<ProtocolOption> All { get; } = new List<ProtocolOption>
    {
        new(DeviceProtocol.Isapi, "Protocol_Hikvision"),
        new(DeviceProtocol.HikvisionSdk, "Protocol_HikvisionSdk"),
        new(DeviceProtocol.Onvif, "Protocol_Onvif")
    };

    public DeviceProtocol Value { get; }

    public string Label => Localizer.Text(_labelKey);

    private ProtocolOption(DeviceProtocol value, string labelKey)
    {
        Value = value;
        _labelKey = labelKey;
        Localizer.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public static ProtocolOption Find(DeviceProtocol value)
    {
        return All.FirstOrDefault(option => option.Value == value) ?? All[0];
    }
}
