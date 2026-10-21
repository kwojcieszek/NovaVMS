using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Tests;

/// <summary>
/// Factory that records the clients it creates and the device sets passed for connection cleanup.
/// </summary>
internal sealed class FakeDeviceClientFactory : IDeviceClientFactory
{
    public List<FakeDeviceClient> Created { get; } = new();
    public Action<FakeDeviceClient>? ConfigureClient { get; set; }
    public Action? BeforeRelease { get; set; }

    public List<IReadOnlyCollection<Device>> Releases { get; } = new();

    public TaskCompletionSource ReleaseCalled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IDeviceClient Create(Device device)
    {
        var client = new FakeDeviceClient(device);
        ConfigureClient?.Invoke(client);
        Created.Add(client);
        return client;
    }

    public void ReleaseUnusedConnections(IReadOnlyCollection<Device> devices)
    {
        BeforeRelease?.Invoke();
        lock (Releases)
        {
            Releases.Add(devices);
        }

        ReleaseCalled.TrySetResult();
    }

    public void ResetReleaseSignal()
    {
        ReleaseCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
