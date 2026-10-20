namespace NovaVMS.Core.Abstractions;

/// <summary>Supports explicit login without throwing for a rejected connection.</summary>
public interface IDeviceConnectionClient
{
    Task<DeviceConnectionResult> TryConnectAsync(CancellationToken cancellationToken = default);
}

public sealed record DeviceConnectionResult(Exception? Error)
{
    public bool Success => Error is null;
}
