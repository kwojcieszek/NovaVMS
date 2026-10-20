using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.Onvif;

/// <summary>
/// SOAP 1.2 transport for ONVIF services with WS-Security UsernameToken (password digest).
/// HTTP Digest authentication is answered as well, because some devices use it instead of WS-Security.
/// The device clock offset is measured once, so the token timestamp is accepted even when clocks differ.
/// </summary>
internal sealed class OnvifSoapClient : IDisposable
{
    private const string SoapMediaType = "application/soap+xml";

    private readonly HttpClient _httpClient;
    private readonly string _username;
    private readonly string _password;
    private TimeSpan? _clockOffset;

    public OnvifSoapClient(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _username = device.Username;
        _password = device.Password;

        var scheme = device.UseHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var baseUri = new UriBuilder(scheme, device.Host, device.HttpPort).Uri;
        var credential = new NetworkCredential(device.Username, device.Password);
        var handler = new SocketsHttpHandler
        {
            Credentials = new CredentialCache { { baseUri, "Digest", credential } },
            PreAuthenticate = false
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(10)
        };

        DeviceServiceUri = new Uri(baseUri, "onvif/device_service");
    }

    public Uri DeviceServiceUri { get; }

    public async Task<XElement> CallAsync(Uri serviceUri, XElement request, CancellationToken cancellationToken)
    {
        _clockOffset ??= await ReadClockOffsetAsync(cancellationToken).ConfigureAwait(false);
        return await SendAsync(serviceUri, request, authenticate: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DeviceCommunicationException(ProtocolText.Format("HttpStatus", (int)response.StatusCode, uri.AbsolutePath), response.StatusCode);
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new DeviceCommunicationException(ProtocolText.Format("ConnectionFailed", exception.Message), exception);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>
    /// PasswordDigest = Base64(SHA1(nonce + created + password)), as defined by the WS-Security UsernameToken profile.
    /// </summary>
    public static string CreatePasswordDigest(byte[] nonce, string created, string password)
    {
        var createdBytes = Encoding.UTF8.GetBytes(created);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var buffer = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
        nonce.CopyTo(buffer, 0);
        createdBytes.CopyTo(buffer, nonce.Length);
        passwordBytes.CopyTo(buffer, nonce.Length + createdBytes.Length);
        return Convert.ToBase64String(SHA1.HashData(buffer));
    }

    // GetSystemDateAndTime does not require authentication; it lets the token use the device's clock.
    private async Task<TimeSpan> ReadClockOffsetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new XElement(OnvifNamespaces.Device + "GetSystemDateAndTime");
            var response = await SendAsync(DeviceServiceUri, request, authenticate: false, cancellationToken).ConfigureAwait(false);
            var utc = response.DescendantsByLocalName("UTCDateTime").FirstOrDefault();
            if (utc is null)
            {
                return TimeSpan.Zero;
            }

            var date = utc.DescendantsByLocalName("Date").First();
            var time = utc.DescendantsByLocalName("Time").First();
            var deviceUtc = new DateTime(
                int.Parse(date.ChildValue("Year"), CultureInfo.InvariantCulture),
                int.Parse(date.ChildValue("Month"), CultureInfo.InvariantCulture),
                int.Parse(date.ChildValue("Day"), CultureInfo.InvariantCulture),
                int.Parse(time.ChildValue("Hour"), CultureInfo.InvariantCulture),
                int.Parse(time.ChildValue("Minute"), CultureInfo.InvariantCulture),
                int.Parse(time.ChildValue("Second"), CultureInfo.InvariantCulture),
                DateTimeKind.Utc);

            return deviceUtc - DateTime.UtcNow;
        }
        catch (Exception exception) when (exception is DeviceCommunicationException or FormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            // Devices without a readable clock: use the local time.
            return TimeSpan.Zero;
        }
    }

    private async Task<XElement> SendAsync(Uri serviceUri, XElement request, bool authenticate, CancellationToken cancellationToken)
    {
        var envelope = new XElement(OnvifNamespaces.Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", OnvifNamespaces.Soap),
            new XAttribute(XNamespace.Xmlns + "tt", OnvifNamespaces.Schema),
            authenticate ? new XElement(OnvifNamespaces.Soap + "Header", CreateSecurityHeader()) : null,
            new XElement(OnvifNamespaces.Soap + "Body", request));

        try
        {
            using var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, SoapMediaType);
            using var response = await _httpClient.PostAsync(serviceUri, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new DeviceCommunicationException(ProtocolText.Get("InvalidCredentials"), response.StatusCode);
            }

            var document = TryParse(body);
            var fault = document?.DescendantsByLocalName("Fault").FirstOrDefault();
            if (fault is not null)
            {
                throw new DeviceCommunicationException(DescribeFault(fault), response.StatusCode);
            }

            if (!response.IsSuccessStatusCode || document?.Root is null)
            {
                throw new DeviceCommunicationException(ProtocolText.Format("HttpStatus", (int)response.StatusCode, serviceUri.AbsolutePath), response.StatusCode);
            }

            var responseBody = document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "Body");
            return responseBody?.Elements().FirstOrDefault() ?? new XElement("Empty");
        }
        catch (HttpRequestException exception)
        {
            throw new DeviceCommunicationException(ProtocolText.Format("ConnectionFailed", exception.Message), exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceCommunicationException(ProtocolText.Get("Timeout"), exception);
        }
    }

    private XElement CreateSecurityHeader()
    {
        var nonce = RandomNumberGenerator.GetBytes(16);
        var created = (DateTime.UtcNow + (_clockOffset ?? TimeSpan.Zero)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var security = OnvifNamespaces.Security;

        return new XElement(security + "Security",
            new XAttribute(OnvifNamespaces.Soap + "mustUnderstand", "1"),
            new XElement(security + "UsernameToken",
                new XElement(security + "Username", _username),
                new XElement(security + "Password",
                    new XAttribute("Type", OnvifNamespaces.PasswordDigestType),
                    CreatePasswordDigest(nonce, created, _password)),
                new XElement(security + "Nonce",
                    new XAttribute("EncodingType", OnvifNamespaces.Base64EncodingType),
                    Convert.ToBase64String(nonce)),
                new XElement(OnvifNamespaces.Utility + "Created", created)));
    }

    private static XDocument? TryParse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(body);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string DescribeFault(XElement fault)
    {
        var reason = fault.DescendantsByLocalName("Text").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            reason = fault.DescendantsByLocalName("Value").LastOrDefault()?.Value.Trim() ?? string.Empty;
        }

        // Authentication failures are reported as SOAP faults by most cameras.
        if (reason.Contains("auth", StringComparison.OrdinalIgnoreCase) || reason.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase))
        {
            return ProtocolText.Get("InvalidCredentials");
        }

        return ProtocolText.Format("OnvifFault", reason);
    }
}
