using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Common;

/// <summary>
/// What a card has already spent today (approved swipes), per channel and abroad.
/// Input for the cardholder's daily limits (Module 6).
/// </summary>
public sealed class DailySpend
{
    public static readonly DailySpend None = new(new Dictionary<TransactionChannel, decimal>(), 0m);

    private readonly IReadOnlyDictionary<TransactionChannel, decimal> _byChannel;

    public DailySpend(IReadOnlyDictionary<TransactionChannel, decimal> byChannel, decimal international)
    {
        _byChannel = byChannel;
        International = international;
    }

    /// <summary>Spent today abroad, all channels together.</summary>
    public decimal International { get; }

    public decimal For(TransactionChannel channel) => _byChannel.TryGetValue(channel, out var amount) ? amount : 0m;
}
