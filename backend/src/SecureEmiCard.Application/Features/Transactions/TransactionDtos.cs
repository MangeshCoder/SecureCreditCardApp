using SecureEmiCard.Application.Features.Cards;

namespace SecureEmiCard.Application.Features.Transactions;

/// <summary>
/// What a merchant POS terminal / payment page sends to authorize a purchase.
/// Card-present (PIN) and card-not-present (expiry + CVV) data are both required in this simulator.
/// </summary>
public record SwipeRequest(
    string CardNumber,
    int ExpiryMonth,
    int ExpiryYear,
    string Cvv,
    string Pin,
    string MerchantName,
    string MerchantCategoryCode,
    decimal Amount);

/// <summary>Result of an authorization. A decline is a normal business outcome, so it is HTTP 200, not an error.</summary>
public record SwipeResponse(
    bool Approved,
    string Status,
    string? DeclineReason,
    int? TransactionId,
    string? MaskedCardNumber,
    decimal Amount,
    decimal? AvailableBalance,
    DateTime ProcessedAtUtc);

/// <summary>Repayment / balance load onto a card.</summary>
public record LoadRequest(int CardId, decimal Amount);

public record TransactionDto(
    int TransactionId,
    int CardId,
    string MaskedCardNumber,
    string MerchantName,
    string MerchantCategoryCode,
    decimal Amount,
    string TransactionType,
    string TransactionStatus,
    string? DeclineReason,
    bool IsEmiConverted,
    DateTime TransactionDate);

/// <summary>Returned by load and refund: the new ledger row plus the card's updated balances.</summary>
public record BalanceChangeResponse(TransactionDto Transaction, CardDto Card);

public record MerchantCategoryDto(string Code, string Description);
