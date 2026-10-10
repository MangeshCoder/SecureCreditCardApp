using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Common;

namespace SecureEmiCard.Application.Features.CardControls;

public interface ICardControlRules
{
    CardControlOptions Options { get; }

    /// <summary>Channel, merchant country (default: home country) and whether that is abroad.</summary>
    SwipeOrigin OriginOf(SwipeRequest request);

    /// <summary>UTC moment at which the current business day started (midnight IST by default).</summary>
    DateTime StartOfTodayUtc();
}

public class CardControlRules : ICardControlRules
{
    public CardControlRules(IOptions<CardControlOptions> options) => Options = options.Value;

    public CardControlOptions Options { get; }

    public SwipeOrigin OriginOf(SwipeRequest request)
    {
        var country = string.IsNullOrWhiteSpace(request.MerchantCountry)
            ? Options.HomeCountryCode
            : request.MerchantCountry.Trim().ToUpperInvariant();
        return new SwipeOrigin(request.Channel, country, country != Options.HomeCountryCode);
    }

    public DateTime StartOfTodayUtc() => StartOfBusinessDayUtc(DateTime.UtcNow, Options.BusinessDayUtcOffset);

    /// <summary>
    /// Example with IST (+05:30): at 2026-10-10 20:00 UTC it is already 01:30 on 11 October in India,
    /// so the business day started at 2026-10-10 18:30 UTC.
    /// </summary>
    public static DateTime StartOfBusinessDayUtc(DateTime nowUtc, TimeSpan utcOffset)
    {
        var localMidnight = (nowUtc + utcOffset).Date;
        return DateTime.SpecifyKind(localMidnight - utcOffset, DateTimeKind.Utc);
    }
}
