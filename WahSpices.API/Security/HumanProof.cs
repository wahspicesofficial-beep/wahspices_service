using System.Buffers.Binary;
using System.Numerics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WahSpices.Application.Common;
using WahSpices.Infrastructure.Security;

namespace WahSpices.API.Security;

// ─────────────────────────────────────────────────────────────────────────────
//  Self-hosted proof-of-work "human proof" (no third-party keys needed).
//
//  1. Client: GET /api/security/challenge?scope=auth
//       → { challenge, salt, difficulty, expiresIn, algorithm, turnstileSiteKey? }
//     challenge = base64url(payload).base64url(HMAC-SHA256(serverKey, payload)[0..16])
//     payload   = v1 | 12 random bytes (salt) | expiry | difficulty | scope hash | client-IP hash
//  2. Client finds a decimal nonce so that SHA-256(salt + ":" + nonce) starts with `difficulty` zero bits
//     (Web Worker, see src/services/humanProof.js) and sends   X-PoW-Proof: <challenge>:<nonce>
//  3. [RequireHumanProof("auth")] verifies: signature, expiry (2 min), scope, difficulty ≥ the scope's,
//     same client IP, the hash, and that the challenge was never used before (single use, replay guard).
//
//  What it buys: every protected call costs the caller ~0.1–1 s of CPU and a fresh round-trip, challenges
//  can't be farmed on one IP and spent from another, and nothing can be replayed. A determined attacker
//  with native code can still solve challenges — rate limits + lockouts are the real ceiling; this makes
//  "copy the request from DevTools and loop it" stop working and makes mass abuse measurably expensive.
//
//  Optional: Cloudflare Turnstile on top (Security:Turnstile:SiteKey + SecretKey). Off unless both are set.
// ─────────────────────────────────────────────────────────────────────────────

public static class HumanProofScopes
{
    /// <summary>Default difficulty (leading zero bits) per scope. Override with Security:HumanProof:Difficulty:&lt;scope&gt;.</summary>
    public static readonly IReadOnlyDictionary<string, int> Defaults = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["auth"]       = 18,   // customer register / login / OTP / forgot / reset
        ["admin-auth"] = 18,   // admin login + 2FA
        ["contact"]    = 17,
        ["feedback"]   = 17,
        ["review"]     = 16,
        ["order"]      = 16,   // order placement
        ["track"]      = 15,   // public order tracking
        ["coupon"]     = 12,   // light: coupon apply
        ["quote"]      = 12,   // light: cart quote (called on every cart change)
    };
}

public sealed class HumanProofService
{
    public const string HeaderName = "X-PoW-Proof";
    public const string TurnstileHeader = "X-Captcha-Token";
    private const byte Version = 1;
    private const int PayloadLength = 1 + 12 + 4 + 1 + 2 + 4;

    private readonly byte[] _key;
    private readonly byte[] _ipKey;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<string, long> _used = new();
    private long _lastPurge;

    public HumanProofService(IConfiguration config)
    {
        _config = config;
        _key = SecretHash.DeriveKey(config, "human-proof", "Security:HumanProof:Secret");
        _ipKey = SecretHash.DeriveKey(config, "human-proof-ip", "Security:HumanProof:Secret");
    }

    public bool Enabled => _config.GetValue("Security:HumanProof:Enabled", true);
    private bool BindToIp => _config.GetValue("Security:HumanProof:BindToIp", true);
    public int LifetimeSeconds => Math.Clamp(_config.GetValue("Security:HumanProof:LifetimeSeconds", 120), 30, 600);

    public string? TurnstileSiteKey =>
        !string.IsNullOrWhiteSpace(_config["Security:Turnstile:SiteKey"]) && !string.IsNullOrWhiteSpace(_config["Security:Turnstile:SecretKey"])
            ? _config["Security:Turnstile:SiteKey"] : null;

    public int DifficultyFor(string scope)
    {
        var configured = _config.GetValue<int?>($"Security:HumanProof:Difficulty:{scope}");
        var d = configured ?? (HumanProofScopes.Defaults.TryGetValue(scope, out var v) ? v : 16);
        return Math.Clamp(d, 1, 26);
    }

    public bool IsKnownScope(string scope) => HumanProofScopes.Defaults.ContainsKey(scope);

    public (string challenge, string salt, int difficulty, int expiresIn) Issue(string scope, string? ip)
    {
        var d = DifficultyFor(scope);
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        RandomNumberGenerator.Fill(payload.AsSpan(1, 12));
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(13, 4), (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + LifetimeSeconds));
        payload[17] = (byte)d;
        ScopeHash(scope).CopyTo(payload, 18);
        IpHash(ip).CopyTo(payload, 20);
        var mac = HMACSHA256.HashData(_key, payload).AsSpan(0, 16).ToArray();
        var token = SecretHash.Base64Url(payload) + "." + SecretHash.Base64Url(mac);
        return (token, SecretHash.Base64Url(payload[1..13]), d, LifetimeSeconds);
    }

    /// <summary>Verifies "challenge:nonce". Returns null when valid (and consumes it), else an error code.</summary>
    public string? Verify(string? header, string scope, string? ip)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Length > 200) return "HUMAN_PROOF_REQUIRED";
        var sep = header.LastIndexOf(':');
        if (sep <= 0) return "HUMAN_PROOF_INVALID";
        var token = header[..sep];
        var nonce = header[(sep + 1)..];
        if (nonce.Length is 0 or > 16 || !nonce.All(char.IsAsciiDigit)) return "HUMAN_PROOF_INVALID";

        var dot = token.IndexOf('.');
        if (dot <= 0) return "HUMAN_PROOF_INVALID";
        byte[] payload, mac;
        try { payload = SecretHash.FromBase64Url(token[..dot]); mac = SecretHash.FromBase64Url(token[(dot + 1)..]); }
        catch (FormatException) { return "HUMAN_PROOF_INVALID"; }
        if (payload.Length != PayloadLength || mac.Length != 16 || payload[0] != Version) return "HUMAN_PROOF_INVALID";

        var expected = HMACSHA256.HashData(_key, payload).AsSpan(0, 16);
        if (!CryptographicOperations.FixedTimeEquals(expected, mac)) return "HUMAN_PROOF_INVALID";

        var exp = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(13, 4));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (exp < now) return "HUMAN_PROOF_EXPIRED";
        var d = payload[17];
        if (d < DifficultyFor(scope)) return "HUMAN_PROOF_INVALID";
        if (!payload.AsSpan(18, 2).SequenceEqual(ScopeHash(scope))) return "HUMAN_PROOF_INVALID";
        if (BindToIp && !payload.AsSpan(20, 4).SequenceEqual(IpHash(ip))) return "HUMAN_PROOF_INVALID";

        var salt = SecretHash.Base64Url(payload[1..13]);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(salt + ":" + nonce));
        if (LeadingZeroBits(hash) < d) return "HUMAN_PROOF_INVALID";

        Purge(now);
        if (!_used.TryAdd(salt, exp)) return "HUMAN_PROOF_REPLAYED";   // single use
        return null;
    }

    public static int LeadingZeroBits(ReadOnlySpan<byte> hash)
    {
        var bits = 0;
        foreach (var b in hash)
        {
            if (b == 0) { bits += 8; continue; }
            bits += BitOperations.LeadingZeroCount(b) - 24;
            break;
        }
        return bits;
    }

    private static byte[] ScopeHash(string scope) => SHA256.HashData(Encoding.UTF8.GetBytes(scope.ToLowerInvariant()))[..2];
    private byte[] IpHash(string? ip) => HMACSHA256.HashData(_ipKey, Encoding.UTF8.GetBytes(BindToIp ? ip ?? "?" : "-"))[..4];

    private void Purge(long now)
    {
        if (now - Interlocked.Read(ref _lastPurge) < 30) return;
        Interlocked.Exchange(ref _lastPurge, now);
        foreach (var kv in _used)
            if (kv.Value < now) _used.TryRemove(kv.Key, out _);
    }
}

/// <summary>Optional Cloudflare Turnstile verification (only when both keys are configured).</summary>
public sealed class TurnstileVerifier(IHttpClientFactory http, IConfiguration config, ILogger<TurnstileVerifier> logger)
{
    public async Task<bool> VerifyAsync(string? token, string? ip)
    {
        var secret = config["Security:Turnstile:SecretKey"];
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || string.IsNullOrWhiteSpace(secret)) return false;
        try
        {
            var client = http.CreateClient("turnstile");
            var form = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
            if (!string.IsNullOrEmpty(ip)) form["remoteip"] = ip;
            using var res = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", new FormUrlEncodedContent(form));
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Turnstile verification failed");
            return false;
        }
    }
}

/// <summary>
/// Requires a solved proof-of-work challenge for this action (header X-PoW-Proof) — and a Turnstile token too
/// when Turnstile is configured. Runs as an authorization filter, i.e. before model binding.
///   [RequireHumanProof("auth")]   — scope decides the difficulty (see HumanProofScopes)
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequireHumanProofAttribute(string scope) : Attribute, IAsyncAuthorizationFilter
{
    public string Scope { get; } = scope;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var svc = http.RequestServices.GetRequiredService<HumanProofService>();
        if (!svc.Enabled) return;

        var ip = ClientIp.Get(http);
        var error = svc.Verify(http.Request.Headers[HumanProofService.HeaderName].FirstOrDefault(), Scope, ip);
        if (error == null && svc.TurnstileSiteKey != null)
        {
            var ts = http.RequestServices.GetRequiredService<TurnstileVerifier>();
            if (!await ts.VerifyAsync(http.Request.Headers[HumanProofService.TurnstileHeader].FirstOrDefault(), ip))
                error = "TURNSTILE_FAILED";
        }
        if (error == null) return;

        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HumanProof")
            .LogInformation("Human proof rejected ({Code}) for {Method} {Path} from {Ip}", error, http.Request.Method, http.Request.Path, ip);
        context.Result = new ObjectResult(ApiResponse<object>.Fail(error,
            error == "HUMAN_PROOF_REQUIRED"
                ? "Security check required. Please refresh the page and try again."
                : "Security check failed. Please try again."))
        { StatusCode = StatusCodes.Status428PreconditionRequired };
    }
}

[ApiController]
[AllowAnonymous]
[Route("api/security")]
[Produces("application/json")]
public class SecurityController(HumanProofService proof) : ControllerBase
{
    /// <summary>Issues a short-lived, single-use proof-of-work challenge for the given scope.</summary>
    [HttpGet("challenge")]
    public IActionResult Challenge([FromQuery] string? scope)
    {
        scope = (scope ?? "").Trim().ToLowerInvariant();
        if (!proof.IsKnownScope(scope))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION", "Unknown challenge scope."));
        var (challenge, salt, difficulty, expiresIn) = proof.Issue(scope, ClientIp.Get(HttpContext));
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse<object>.Ok(new
        {
            challenge, salt, difficulty, expiresIn,
            algorithm = "sha256-leading-zero-bits",
            enabled = proof.Enabled,
            turnstileSiteKey = proof.TurnstileSiteKey,
        }));
    }
}
