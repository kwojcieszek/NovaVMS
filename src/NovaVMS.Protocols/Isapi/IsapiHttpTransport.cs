using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// HTTP layer for ISAPI: Digest/Basic authentication, error handling and XML.
/// </summary>
internal sealed class IsapiHttpTransport : IIsapiTransport
{
    private const string XmlMediaType = "application/xml";

    private readonly HttpClient _httpClient;

    public IsapiHttpTransport(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var scheme = device.UseHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var baseUri = new UriBuilder(scheme, device.Host, device.HttpPort).Uri;
        var credential = new NetworkCredential(device.Username, device.Password);
        var credentials = new CredentialCache
        {
            { baseUri, "Digest", credential },
            { baseUri, "Basic", credential }
        };

        var handler = new SocketsHttpHandler
        {
            Credentials = credentials,
            PreAuthenticate = false
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public Task<XDocument> GetXmlAsync(string path, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async token =>
        {
            using var response = await SendAsync(HttpMethod.Get, path, null, token).ConfigureAwait(false);
            return await ReadXmlAsync(response, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<XDocument> PostXmlAsync(string path, XElement body, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async token =>
        {
            using var response = await SendAsync(HttpMethod.Post, path, body, token).ConfigureAwait(false);
            return await ReadXmlAsync(response, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task PutAsync(string path, XElement? body, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async token =>
        {
            using var response = await SendAsync(HttpMethod.Put, path, body, token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async token =>
        {
            using var response = await SendAsync(HttpMethod.Get, path, null, token).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, XElement? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, XmlMediaType);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var statusCode = response.StatusCode;
        response.Dispose();
        throw new DeviceCommunicationException(DescribeStatus(statusCode, path), statusCode);
    }

    private static async Task<XDocument> ReadXmlAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
    }

    private static string DescribeStatus(HttpStatusCode statusCode, string path)
    {
        switch (statusCode)
        {
            case HttpStatusCode.Unauthorized:
            {
                return ProtocolText.Get("InvalidCredentials");
            }
            case HttpStatusCode.Forbidden:
            {
                return ProtocolText.Get("Forbidden");
            }
            case HttpStatusCode.NotFound:
            {
                return ProtocolText.Format("FunctionNotSupported", path);
            }
            default:
            {
                return ProtocolText.Format("HttpStatus", (int)statusCode, path);
            }
        }
    }

    private static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new DeviceCommunicationException(ProtocolText.Format("ConnectionFailed", exception.Message), exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceCommunicationException(ProtocolText.Get("Timeout"), exception);
        }
        catch (XmlException exception)
        {
            throw new DeviceCommunicationException(ProtocolText.Get("InvalidXml"), exception);
        }
    }
}
