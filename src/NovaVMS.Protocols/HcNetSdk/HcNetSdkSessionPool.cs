using System.Runtime.InteropServices;
using NovaVMS.Core.Models;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Shares one SDK login per device and credentials. Clients are short-lived, but a login takes a few seconds
/// and every login counts against the device's failed-login lockout, so sessions are kept until the
/// application closes or a network/credential error invalidates them.
/// </summary>
internal static class HcNetSdkSessionPool
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, HcNetSdkSession> Sessions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SemaphoreSlim> LoginLocks = new(StringComparer.Ordinal);

    public static async Task<HcNetSdkSession> AcquireAsync(Device device, CancellationToken cancellationToken)
    {
        // Legacy operations require an established session. Application entry points use TryAcquireAsync
        // through TryConnectAsync, so an ordinary rejected login is returned before calling those operations.
        var result = await TryAcquireAsync(device, cancellationToken).ConfigureAwait(false);
        return result.Session ?? throw result.Error!;
    }

    public static async Task<HcNetSdkLoginResult> TryAcquireAsync(Device device, CancellationToken cancellationToken)
    {
        HcNetSdkRuntime.EnsureInitialized();

        var key = BuildKey(device);
        SemaphoreSlim loginLock;
        lock (SyncRoot)
        {
            if (Sessions.TryGetValue(key, out var existing))
            {
                return new HcNetSdkLoginResult(existing, null);
            }

            if (!LoginLocks.TryGetValue(key, out loginLock!))
            {
                loginLock = new SemaphoreSlim(1, 1);
                LoginLocks[key] = loginLock;
            }
        }

        await loginLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (SyncRoot)
            {
                if (Sessions.TryGetValue(key, out var existing))
                {
                    return new HcNetSdkLoginResult(existing, null);
                }
            }

            var result = await Task.Run(() => Login(device), cancellationToken).ConfigureAwait(false);
            if (result.Session is not { } session)
            {
                return result;
            }
            lock (SyncRoot)
            {
                RemoveStaleSessions(device);
                Sessions[key] = session;
            }

            return result;
        }
        finally
        {
            loginLock.Release();
        }
    }

    /// <summary>
    /// Drops the session after an error that makes it unusable; the next request logs in again.
    /// </summary>
    public static void Invalidate(HcNetSdkSession session)
    {
        lock (SyncRoot)
        {
            var key = Sessions.FirstOrDefault(pair => ReferenceEquals(pair.Value, session)).Key;
            if (key is null)
            {
                return;
            }

            Sessions.Remove(key);
        }

        HcNetSdkNative.Logout(session.UserId);
    }

    public static void LogoutAll()
    {
        List<HcNetSdkSession> sessions;
        lock (SyncRoot)
        {
            sessions = Sessions.Values.ToList();
            Sessions.Clear();
        }

        foreach (var session in sessions)
        {
            HcNetSdkNative.Logout(session.UserId);
        }
    }

    private static HcNetSdkLoginResult Login(Device device)
    {
        var deviceInfo = Marshal.AllocHGlobal(HcNetSdkSession.DeviceInfoBufferSize);
        try
        {
            Marshal.Copy(new byte[HcNetSdkSession.DeviceInfoBufferSize], 0, deviceInfo, HcNetSdkSession.DeviceInfoBufferSize);

            var userId = HcNetSdkNative.Login(device.Host.Trim(), (ushort)device.SdkPort, device.Username, device.Password, deviceInfo);
            if (userId < 0)
            {
                return new HcNetSdkLoginResult(null, HcNetSdkErrors.LastError(device.Host, device.SdkPort));
            }

            return new HcNetSdkLoginResult(new HcNetSdkSession(userId, device.Host, device.SdkPort, deviceInfo), null);
        }
        finally
        {
            Marshal.FreeHGlobal(deviceInfo);
        }
    }

    /// <summary>
    /// Logs out of sessions opened for the same device with previous settings (e.g. before a password change).
    /// </summary>
    private static void RemoveStaleSessions(Device device)
    {
        var prefix = device.Id.ToString("N") + "|";
        foreach (var stale in Sessions.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            Sessions.Remove(stale.Key);
            HcNetSdkNative.Logout(stale.Value.UserId);
        }
    }

    private static string BuildKey(Device device)
    {
        return string.Join("|", device.Id.ToString("N"), device.Host.Trim(), device.SdkPort, device.Username, device.Password);
    }
}
