namespace WahSpices.Infrastructure.Email;

/// <summary>
/// Counts transactional emails (OTP, order, account, contact replies …) sent in the last 24 hours, so the campaign
/// throttle can leave them headroom on the shared Gmail account. Transactional mail itself is never throttled.
/// In-process only: after a restart the count starts from zero (the reserve still protects the first 24 h).
/// </summary>
public interface ITransactionalEmailCounter
{
    void Record();
    int CountLast24Hours();
}

public sealed class TransactionalEmailCounter : ITransactionalEmailCounter
{
    private readonly Queue<DateTime> _sent = new();
    private readonly object _lock = new();

    public void Record()
    {
        lock (_lock)
        {
            _sent.Enqueue(DateTime.UtcNow);
            Trim();
        }
    }

    public int CountLast24Hours()
    {
        lock (_lock)
        {
            Trim();
            return _sent.Count;
        }
    }

    private void Trim()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        while (_sent.Count > 0 && _sent.Peek() < cutoff) _sent.Dequeue();
    }
}
