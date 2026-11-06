namespace NovaVMS.Core.Abstractions;

public sealed record DeviceConnectionResult(Exception? Error)
{
    public bool Success => Error is null;
}
