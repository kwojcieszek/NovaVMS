using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>Basic and Digest (MD5/SHA-256, with auth qop) for RTSP, never placed in a URL.</summary>
internal sealed class RtspAuthentication(string challenge, string username, string password)
{
    private int _nonceCount;
    private readonly string _clientNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public string Build(string method, Uri uri)
    {
        if (challenge.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        if (!challenge.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase)) throw new IOException("RTSP: unsupported authentication scheme.");
        var values = Regex.Matches(challenge[7..], "([\\w-]+)\\s*=\\s*(?:\"([^\"]*)\"|([^,\\s]+))")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value, StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("realm", out var realm) || !values.TryGetValue("nonce", out var nonce))
            throw new IOException("RTSP: invalid authentication challenge.");
        var algorithm = values.GetValueOrDefault("algorithm", "MD5");
        var session = algorithm.EndsWith("-sess", StringComparison.OrdinalIgnoreCase);
        var hashName = session ? algorithm[..^5] : algorithm;
        string Hash(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return Convert.ToHexString(hashName.ToUpperInvariant() switch
            {
                "MD5" => MD5.HashData(bytes),
                "SHA-256" => SHA256.HashData(bytes),
                _ => throw new IOException("RTSP: unsupported Digest algorithm.")
            }).ToLowerInvariant();
        }
        string Quote(string text)
        {
            if (text.Contains('\r') || text.Contains('\n')) throw new IOException("RTSP: invalid authentication value.");
            return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
        var ha1 = Hash($"{username}:{realm}:{password}");
        if (session) ha1 = Hash($"{ha1}:{nonce}:{_clientNonce}");
        var ha2 = Hash($"{method}:{uri.AbsoluteUri}");
        var nc = (++_nonceCount).ToString("x8");
        var qop = values.GetValueOrDefault("qop");
        if (qop is not null && !qop.Split(',').Any(value => value.Trim().Equals("auth", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("RTSP: unsupported Digest protection mode.");
        var response = qop is null ? Hash($"{ha1}:{nonce}:{ha2}") : Hash($"{ha1}:{nonce}:{nc}:{_clientNonce}:auth:{ha2}");
        var result = $"Digest username={Quote(username)}, realm={Quote(realm)}, nonce={Quote(nonce)}, uri={Quote(uri.AbsoluteUri)}, response={Quote(response)}, algorithm={algorithm}";
        if (qop is not null) result += $", qop=auth, nc={nc}, cnonce={Quote(_clientNonce)}";
        else if (session) result += $", cnonce={Quote(_clientNonce)}";
        if (values.TryGetValue("opaque", out var opaque)) result += $", opaque={Quote(opaque)}";
        return result;
    }
}
