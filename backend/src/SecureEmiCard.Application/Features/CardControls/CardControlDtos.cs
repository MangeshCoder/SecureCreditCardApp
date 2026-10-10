namespace SecureEmiCard.Application.Features.CardControls;

/// <summary>One switch with its limit and what was already spent through it today.</summary>
public record ChannelControlDto(bool Enabled, decimal? DailyLimit, decimal SpentToday);

public record CardControlsDto(
    int CardId,
    string MaskedCardNumber,
    string CardStatus,
    bool IsLocked,
    DateTime? LockedAt,
    decimal CreditLimit,
    decimal ContactlessPerTransactionLimit, // bank rule - the cardholder cannot change it
    string HomeCountryCode,
    ChannelControlDto Pos,
    ChannelControlDto Online,
    ChannelControlDto Contactless,
    ChannelControlDto Atm,
    ChannelControlDto International,
    DateTime UpdatedAt);

/// <summary>DailyLimit null = no extra limit (only the available credit applies).</summary>
public record ChannelSettingRequest(bool Enabled, decimal? DailyLimit);

/// <summary>The full set of controls; every switch is sent each time (PUT replaces the settings).</summary>
public record UpdateCardControlsRequest(
    ChannelSettingRequest Pos,
    ChannelSettingRequest Online,
    ChannelSettingRequest Contactless,
    ChannelSettingRequest Atm,
    ChannelSettingRequest International);
