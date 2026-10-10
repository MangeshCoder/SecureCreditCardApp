using System.Collections.Concurrent;

namespace SecureEmiCard.Infrastructure.Notifications;

/// <summary>One SMS or e-mail "sent" by the simulator.</summary>
public record DevMessage(long Id, string Channel, string To, string? Subject, string Body, DateTime SentAtUtc);

/// <summary>
/// DEVELOPMENT ONLY: the "phone" of the message simulator. Keeps the last 200 messages in memory so you can
/// read one-time codes and alerts on the Phone simulator page. Never used when a real provider is configured.
/// </summary>
public class DevMessageOutbox
{
    private const int Capacity = 200;
    private readonly ConcurrentQueue<DevMessage> _messages = new();
    private long _nextId;

    public DevMessage Add(string channel, string to, string? subject, string body)
    {
        var message = new DevMessage(Interlocked.Increment(ref _nextId), channel, to, subject, body, DateTime.UtcNow);
        _messages.Enqueue(message);
        while (_messages.Count > Capacity && _messages.TryDequeue(out _)) { }
        return message;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<DevMessage> Latest(int count) =>
        _messages.Reverse().Take(Math.Clamp(count, 1, Capacity)).ToList();
}
