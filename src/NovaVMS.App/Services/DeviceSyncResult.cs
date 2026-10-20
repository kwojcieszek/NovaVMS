using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>An expected device connection failure is data, not a failed UI command.</summary>
public sealed record DeviceSyncResult(bool Success, DeviceInfo? Info, string? ErrorMessage);
