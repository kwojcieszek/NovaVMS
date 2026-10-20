using System.Net;

namespace NovaVMS.Core.Exceptions;

/// <summary>
/// Communication failure with a device (network, authentication, invalid response).
/// </summary>
public sealed class DeviceCommunicationException : Exception
{
    public DeviceCommunicationException(string message)
        : base(message)
    {
    }

    public DeviceCommunicationException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public DeviceCommunicationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// HTTP status returned by the device when the failure comes from an HTTP response.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }
}
