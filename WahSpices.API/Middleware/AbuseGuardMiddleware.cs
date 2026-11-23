using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using WahSpices.API.Extensions;

namespace WahSpices.API.Middleware;

/// <summary>
/// Detects scripted probing/abuse per client IP: counts 400/401/403/404/405/429 responses on /api
/// within a sliding window and, past the threshold, blocks that IP for N minutes (HTTP 429 + Retry-After).
/// Typical triggers: credential stuffing (401s), ID enumeration (403/404), bots ignoring rate limits (429).
/// Requests made with a valid ADMIN token are never counted.
///
/// State is in-memory (fine for one server). For several servers behind a load balancer move
/// <see cref="AbuseGuardStore"/> to Redis (INCR + EXPIRE per ip/window, SET block:{ip} EX) so all nodes share it.
/// </summary>
public class AbuseGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AbuseGuardStore _store;
    private readonly AbuseGuardSettings _opt;
    private readonly ILogger<AbuseGuardMiddleware> _logger;
    private readonly bool _isDev;
    private readonly HashSet<int> _counted;
    private readonly HashSet<string> _exempt;

    public AbuseGuardMiddleware(RequestDelegate next, AbuseGuardStore store, IOptions<HttpSecurityOptions> opt,
        ILogger<AbuseGuardMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _store = store;
        _opt = opt.Value.AbuseGuard;
        _logger = logger;
        _isDev = env.IsDevelopment();
        _counted = _opt.CountedStatusCodes.ToHashSet();
        _exempt = _opt.ExemptIps.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ipAddr = context.Connection.RemoteIpAddress;
        if (!_opt.Enabled || ipAddr == null || IsExempt(ipAddr))
        {
            await _next(context);
            return;
        }
        var ip = (ipAddr.IsIPv4MappedToIPv6 ? ipAddr.MapToIPv4() : ipAddr).ToString();

        var blockedUntil = _store.BlockedUntil(ip);
        if (blockedUntil != null)
        {
            var retry = Math.Max(1, (int)Math.Ceiling((blockedUntil.Value - DateTime.UtcNow).TotalSeconds));
            context.Response.Headers.RetryAfter = retry.ToString();
            await RequestHardeningMiddleware.WriteError(context, 429, "IP_TEMPORARILY_BLOCKED",
                $"Too many failed requests from your network. Try again in {Math.Ceiling(retry / 60.0)} minute(s).");
            return;
        }

        await _next(context);

        var status = context.Response.StatusCode;
        if (!_counted.Contains(status)) return;
        if (!context.Request.Path.StartsWithSegments(_opt.PathPrefix)) return;
        if (context.User?.FindFirst("admin_id") != null && context.User.FindFirst("temp_2fa") == null) return;

        var count = _store.Hit(ip, TimeSpan.FromSeconds(_opt.WindowSeconds));
        if (count >= _opt.Threshold)
        {
            _store.Block(ip, TimeSpan.FromMinutes(_opt.BlockMinutes), count, $"{context.Request.Method} {context.Request.Path} → {status}");
            _logger.LogWarning("AbuseGuard: blocked {Ip} for {Minutes} min after {Count} suspicious responses in {Window}s (last: {Method} {Path} → {Status})",
                ip, _opt.BlockMinutes, count, _opt.WindowSeconds, context.Request.Method, context.Request.Path, status);
        }
    }

    private bool IsExempt(IPAddress ip)
    {
        if (_isDev && _opt.ExemptLoopbackInDevelopment && IPAddress.IsLoopback(ip)) return true;
        var s = (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
        return _exempt.Contains(s);
    }
}

/// <summary>In-memory sliding-window counters + temporary blocks (singleton).</summary>
public class AbuseGuardStore
{
    private sealed class Window { public readonly Queue<DateTime> Hits = new(); }
    public record BlockInfo(string Ip, DateTime BlockedAt, DateTime Until, int Count, string LastRequest);

    private readonly ConcurrentDictionary<string, Window> _windows = new();
    private readonly ConcurrentDictionary<string, BlockInfo> _blocks = new();
    private DateTime _lastSweep = DateTime.UtcNow;
    private const int MaxTrackedIps = 100_000;

    public int Hit(string ip, TimeSpan window)
    {
        Sweep(window);
        var now = DateTime.UtcNow;
        var w = _windows.GetOrAdd(ip, _ => new Window());
        lock (w)
        {
            w.Hits.Enqueue(now);
            while (w.Hits.Count > 0 && w.Hits.Peek() < now - window) w.Hits.Dequeue();
            if (w.Hits.Count > 10_000) w.Hits.Dequeue();
            return w.Hits.Count;
        }
    }

    public void Block(string ip, TimeSpan duration, int count, string lastRequest)
    {
        var now = DateTime.UtcNow;
        _blocks[ip] = new BlockInfo(ip, now, now + duration, count, lastRequest);
        _windows.TryRemove(ip, out _);
    }

    public DateTime? BlockedUntil(string ip)
    {
        if (!_blocks.TryGetValue(ip, out var b)) return null;
        if (b.Until > DateTime.UtcNow) return b.Until;
        _blocks.TryRemove(ip, out _);
        return null;
    }

    public IReadOnlyList<BlockInfo> ActiveBlocks()
    {
        var now = DateTime.UtcNow;
        return _blocks.Values.Where(b => b.Until > now).OrderByDescending(b => b.BlockedAt).ToList();
    }

    public bool Unblock(string ip) => _blocks.TryRemove(ip, out _) | _windows.TryRemove(ip, out _);

    private void Sweep(TimeSpan window)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSweep < TimeSpan.FromMinutes(1) && _windows.Count < MaxTrackedIps) return;
        _lastSweep = now;
        foreach (var (ip, w) in _windows)
        {
            bool empty;
            lock (w) empty = w.Hits.Count == 0 || w.Hits.Last() < now - window;
            if (empty) _windows.TryRemove(ip, out _);
        }
        foreach (var (ip, b) in _blocks)
            if (b.Until <= now) _blocks.TryRemove(ip, out _);
    }
}
