using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Isapi;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Sends ISAPI requests through the SDK connection (NET_DVR_STDXMLConfig), so a device reachable only
/// on the SDK port (8000) can still be configured, searched and controlled with the regular ISAPI services.
/// </summary>
internal sealed class HcNetSdkIsapiTransport : IIsapiTransport
{
    private const int OutputBufferSize = 4 * 1024 * 1024;
    private const int StatusBufferSize = 16 * 1024;
    private const uint ReceiveTimeoutMs = 10000;

    private readonly Device _device;

    public HcNetSdkIsapiTransport(Device device)
    {
        _device = device;
    }

    public async Task<XDocument> GetXmlAsync(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync("GET", path, null, cancellationToken).ConfigureAwait(false);
        return ParseXml(response);
    }

    public async Task<XDocument> PostXmlAsync(string path, XElement body, CancellationToken cancellationToken)
    {
        var response = await SendAsync("POST", path, body, cancellationToken).ConfigureAwait(false);
        return ParseXml(response);
    }

    public Task PutAsync(string path, XElement? body, CancellationToken cancellationToken)
    {
        return SendAsync("PUT", path, body, cancellationToken);
    }

    public Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken)
    {
        return SendAsync("GET", path, null, cancellationToken);
    }

    public void Dispose()
    {
        // The SDK login is shared by all clients of the device (see HcNetSdkSessionPool).
    }

    private async Task<byte[]> SendAsync(string method, string path, XElement? body, CancellationToken cancellationToken)
    {
        var session = await HcNetSdkSessionPool.AcquireAsync(_device, cancellationToken).ConfigureAwait(false);
        var request = $"{method} /{path.TrimStart('/')}\r\n";
        var content = body is null ? null : Encoding.UTF8.GetBytes(body.ToString(SaveOptions.DisableFormatting));

        return await Task.Run(() => Send(session, request, path, content), cancellationToken).ConfigureAwait(false);
    }

    private byte[] Send(HcNetSdkSession session, string request, string path, byte[]? content)
    {
        var requestBytes = Encoding.ASCII.GetBytes(request);
        var requestBuffer = Marshal.AllocHGlobal(requestBytes.Length);
        var contentBuffer = content is null ? IntPtr.Zero : Marshal.AllocHGlobal(content.Length);
        var outputBuffer = Marshal.AllocHGlobal(OutputBufferSize);
        var statusBuffer = Marshal.AllocHGlobal(StatusBufferSize);

        try
        {
            Marshal.Copy(requestBytes, 0, requestBuffer, requestBytes.Length);
            if (content is not null)
            {
                Marshal.Copy(content, 0, contentBuffer, content.Length);
            }

            var input = new NetDvrXmlConfigInput
            {
                RequestUrl = requestBuffer,
                RequestUrlLength = (uint)requestBytes.Length,
                InBuffer = contentBuffer,
                InBufferSize = content is null ? 0u : (uint)content.Length,
                ReceiveTimeoutMs = ReceiveTimeoutMs,
                Reserved = new byte[30]
            };
            input.Size = (uint)Marshal.SizeOf<NetDvrXmlConfigInput>();

            var output = new NetDvrXmlConfigOutput
            {
                OutBuffer = outputBuffer,
                OutBufferSize = OutputBufferSize,
                StatusBuffer = statusBuffer,
                StatusBufferSize = StatusBufferSize,
                Reserved = new byte[32]
            };
            output.Size = (uint)Marshal.SizeOf<NetDvrXmlConfigOutput>();

            Marshal.WriteByte(statusBuffer, 0, 0);
            if (!HcNetSdkNative.StdXmlConfig(session.UserId, ref input, ref output))
            {
                throw CreateFailure(session, path);
            }

            var length = (int)Math.Min(output.ReturnedSize, OutputBufferSize);
            var result = new byte[length];
            Marshal.Copy(outputBuffer, result, 0, length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(statusBuffer);
            Marshal.FreeHGlobal(outputBuffer);
            if (contentBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(contentBuffer);
            }

            Marshal.FreeHGlobal(requestBuffer);
        }
    }

    /// <summary>
    /// Network, credential and lockout errors are reported as they are. Any other failure means the device
    /// rejected the request (older firmware has no ISAPI, or not this function); it is reported as HTTP 404,
    /// like over HTTP, so the ISAPI services and the SDK client can fall back.
    /// </summary>
    private DeviceCommunicationException CreateFailure(HcNetSdkSession session, string path)
    {
        var code = HcNetSdkNative.GetLastError();
        if (HcNetSdkErrors.InvalidatesSession(code))
        {
            HcNetSdkSessionPool.Invalidate(session);
            return HcNetSdkErrors.FromCode(code, _device.Host, _device.SdkPort);
        }

        if (code == HcNetSdkConstants.ErrorUserLocked)
        {
            return HcNetSdkErrors.FromCode(code, _device.Host, _device.SdkPort);
        }

        return new DeviceCommunicationException(ProtocolText.Format("FunctionNotSupported", path), HttpStatusCode.NotFound);
    }

    private static XDocument ParseXml(byte[] response)
    {
        try
        {
            using var stream = new MemoryStream(response);
            return XDocument.Load(stream);
        }
        catch (XmlException exception)
        {
            throw new DeviceCommunicationException(ProtocolText.Get("InvalidXml"), exception);
        }
    }
}
