using System.Net.Sockets;

namespace NovaVMS.Core.Exceptions;

/// <summary>Operational failures that should be reported without terminating the application.</summary>
public static class DeviceFailure
{
    public static bool IsExpected(Exception exception) => exception is DeviceCommunicationException
        or HttpRequestException or SocketException or IOException or TimeoutException
        or NotSupportedException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
}
