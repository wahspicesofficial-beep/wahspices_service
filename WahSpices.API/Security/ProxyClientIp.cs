using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WahSpices.API.Security;

/// <summary>
/// The website (Vercel) proxies /api/* to this API so the refresh cookie is first-party. Behind that proxy every
/// request comes from a Vercel IP; the proxy passes the visitor's real IP in <c>X-Wah-Client-Ip</c> together with
/// a shared secret in <c>X-Wah-Proxy-Key</c> (config <c>Security:ProxySecret</c>, Vercel env API_PROXY_SECRET).
/// Only when the secret matches does the real IP replace the connection IP — so rate limits, lockouts, proof-of-work
/// and the admin IP allow-list keep working per visitor, and nobody can spoof an IP by calling the API directly.
/// Must run first in the pipeline. Both headers are always removed.
/// </summary>
public sealed class ProxyClientIpMiddleware(RequestDelegate next, IConfiguration config, ILogger<ProxyClientIpMiddleware> logger)
{
    public const string IpHeader = "X-Wah-Client-Ip";
    public const string KeyHeader = "X-Wah-Proxy-Key";
    private readonly byte[]? _secret = Secret(config["Security:ProxySecret"]);

    private static byte[]? Secret(string? s) =>
        string.IsNullOrWhiteSpace(s) || s.Trim().Length < 32 ? null : Encoding.UTF8.GetBytes(s.Trim());

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Request.Headers;
        var ipValue = headers[IpHeader].ToString();
        var keyValue = headers[KeyHeader].ToString();
        headers.Remove(IpHeader);
        headers.Remove(KeyHeader);

        if (_secret != null && keyValue.Length > 0 && ipValue.Length > 0)
        {
            var key = Encoding.UTF8.GetBytes(keyValue);
            if (key.Length == _secret.Length && CryptographicOperations.FixedTimeEquals(key, _secret)
                && IPAddress.TryParse(ipValue.Split(',')[0].Trim(), out var ip))
            {
                context.Connection.RemoteIpAddress = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
            }
            else logger.LogWarning("Ignored proxy client-IP header with a wrong key from {Ip}", context.Connection.RemoteIpAddress);
        }
        return next(context);
    }
}
