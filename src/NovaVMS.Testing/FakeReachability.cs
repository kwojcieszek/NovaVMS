using System.Collections.Concurrent;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Testing;

/// <summary>Device reachability answered by the test; every checked host and port is recorded.</summary>
public sealed class FakeReachability : IDeviceReachability
{
    private readonly Func<string, int, bool>? _portCheck;

    /// <summary>Answer for a device's control port; may be replaced while the test runs.</summary>
    public Func<Device, CancellationToken, Task<bool>> Check { get; set; }

    public ConcurrentQueue<string> Hosts { get; } = new();

    public ConcurrentQueue<(string Host, int Port)> Ports { get; } = new();

    public FakeReachability(Func<Device, CancellationToken, Task<bool>> check, Func<string, int, bool>? portCheck = null)
    {
        Check = check;
        _portCheck = portCheck;
    }

    /// <summary>Answers synchronously; single ports are never reachable.</summary>
    public static FakeReachability Answering(Func<Device, bool> check)
    {
        return new FakeReachability((device, _) => Task.FromResult(check(device)));
    }

    public Task<bool> IsReachableAsync(Device device, CancellationToken cancellationToken = default)
    {
        Hosts.Enqueue(device.Host);

        return Check(device, cancellationToken);
    }

    public Task<bool> IsPortReachableAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        Ports.Enqueue((host, port));

        return Task.FromResult(_portCheck?.Invoke(host, port) ?? false);
    }
}
