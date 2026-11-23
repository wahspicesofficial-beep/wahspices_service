using WahSpices.Application.Interfaces.Services;
using WahSpices.Infrastructure.Email;

namespace WahSpices.API.Services;

/// <summary>
/// Drains <see cref="IEmailQueue"/>: composes each job in its own DI scope and sends it through <see cref="IEmailService"/>.
/// Failures are retried with backoff (Email:Queue:MaxAttempts, default 3; delays 15 s, 60 s, …) without blocking the
/// jobs behind them. On shutdown, whatever is still queued gets up to 15 seconds to go out.
/// Logs recipient, subject and outcome only — never message bodies (they can contain codes / reset links).
/// </summary>
public sealed class EmailDispatcher : BackgroundService
{
    private static readonly TimeSpan[] Backoff = { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5) };

    private readonly IEmailQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailDispatcher> _log;
    private readonly int _maxAttempts;

    public EmailDispatcher(IEmailQueue queue, IServiceScopeFactory scopes, IConfiguration config, ILogger<EmailDispatcher> log)
    {
        _queue = queue;
        _scopes = scopes;
        _log = log;
        _maxAttempts = Math.Clamp(config.GetValue<int?>("Email:Queue:MaxAttempts") ?? 3, 1, 10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
                await ProcessAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }

        // graceful drain on shutdown
        using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!drain.IsCancellationRequested && _queue.Reader.TryRead(out var pending))
            await ProcessAsync(pending, drain.Token, finalAttempt: true);
    }

    private async Task ProcessAsync(EmailJob job, CancellationToken ct, bool finalAttempt = false)
    {
        job.Attempts++;
        try
        {
            using var scope = _scopes.CreateScope();
            job.Composed ??= await job.Compose(scope.ServiceProvider, ct);
            if (job.Composed == null)
            {
                _log.LogInformation("Email [{Label}] skipped — nothing to send", job.Label);
                return;
            }
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            await email.SendAsync(job.Composed, ct);
            scope.ServiceProvider.GetService<ITransactionalEmailCounter>()?.Record();   // campaign throttle leaves headroom for these
            _log.LogInformation("Email delivered [{Label}] to {Email}: \"{Subject}\" (attempt {Attempt}, {Ms} ms after queueing)",
                job.Label, job.Composed.ToEmail, job.Composed.Subject, job.Attempts, (int)(DateTime.UtcNow - job.EnqueuedAtUtc).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log.LogWarning("Email [{Label}] not sent — application shutting down", job.Label);
        }
        catch (Exception ex)
        {
            if (!finalAttempt && job.Attempts < _maxAttempts)
            {
                var delay = Backoff[Math.Min(job.Attempts - 1, Backoff.Length - 1)];
                _log.LogWarning(ex, "Email [{Label}] attempt {Attempt}/{Max} failed — retrying in {Delay}s", job.Label, job.Attempts, _maxAttempts, (int)delay.TotalSeconds);
                _ = RequeueLaterAsync(job, delay, ct);
            }
            else
            {
                _log.LogError(ex, "Email [{Label}] FAILED after {Attempt} attempt(s) to {Email}", job.Label, job.Attempts, job.Composed?.ToEmail ?? "(not composed)");
            }
        }
    }

    private async Task RequeueLaterAsync(EmailJob job, TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { _log.LogWarning("Email [{Label}] retry dropped — application shutting down", job.Label); return; }
        _queue.Enqueue(job);
    }
}
