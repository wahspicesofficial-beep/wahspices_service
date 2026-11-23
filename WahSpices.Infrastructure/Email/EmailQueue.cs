using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WahSpices.Application.Interfaces.Services;

namespace WahSpices.Infrastructure.Email;

/// <summary>
/// One unit of email work. <see cref="Compose"/> runs on the background worker inside its own DI scope (so it may load
/// data from the DB after the caller's transaction committed); returning null means "nothing to send".
/// Once composed, the message is kept so retries resend exactly the same content.
/// </summary>
public sealed class EmailJob
{
    public EmailJob(string label, Func<IServiceProvider, CancellationToken, Task<EmailMessage?>> compose)
    {
        Label = label;
        Compose = compose;
    }

    public string Label { get; }
    public Func<IServiceProvider, CancellationToken, Task<EmailMessage?>> Compose { get; }
    public EmailMessage? Composed { get; set; }
    public int Attempts { get; set; }
    public DateTime EnqueuedAtUtc { get; } = DateTime.UtcNow;

    public static EmailJob Ready(string label, EmailMessage message) =>
        new(label, (_, _) => Task.FromResult<EmailMessage?>(message)) { Composed = message };
}

public interface IEmailQueue
{
    /// <summary>Queues the job; never blocks and never throws. Returns false only if the queue is full.</summary>
    bool Enqueue(EmailJob job);
    ChannelReader<EmailJob> Reader { get; }
}

/// <summary>In-process bounded queue (System.Threading.Channels), drained by the EmailDispatcher hosted service.</summary>
public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailJob> _channel = Channel.CreateBounded<EmailJob>(new BoundedChannelOptions(2000)
    {
        FullMode = BoundedChannelFullMode.Wait,   // TryWrite returns false when full → we log and drop instead of blocking a request
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly ILogger<EmailQueue> _log;

    public EmailQueue(ILogger<EmailQueue> log) => _log = log;

    public ChannelReader<EmailJob> Reader => _channel.Reader;

    public bool Enqueue(EmailJob job)
    {
        if (_channel.Writer.TryWrite(job))
        {
            _log.LogInformation("Email queued [{Label}]", job.Label);
            return true;
        }
        _log.LogError("Email queue full — dropped [{Label}]", job.Label);
        return false;
    }
}
