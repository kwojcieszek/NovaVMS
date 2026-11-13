using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>An expected device connection failure is data, not a failed UI command.</summary>
public sealed record DeviceSyncResult
{
    public bool Success { get; init; }

    public DeviceInfo? Info { get; init; }

    public string? ErrorMessage { get; init; }

    public DeviceSyncResult(bool success, DeviceInfo? info, string? errorMessage)
    {
        Success = success;
        Info = info;
        ErrorMessage = errorMessage;
    }
}
