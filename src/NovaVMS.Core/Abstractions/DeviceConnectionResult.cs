namespace NovaVMS.Core.Abstractions;

public sealed record DeviceConnectionResult
{
    public Exception? Error { get; init; }

    public bool Success => Error is null;

    public DeviceConnectionResult(Exception? error)
    {
        Error = error;
    }
}
