namespace SecureEmiCard.Application.Features.Cashback;

public record CashbackRuleDto(string MerchantCategoryCode, string Description, decimal Percentage);

public record CashbackRulesDto(
    IReadOnlyList<CashbackRuleDto> CategoryRules,
    decimal DefaultPercentage,
    decimal MinimumSpend,
    decimal MaxCashbackPerTransaction);

public record CashbackCategoryTotalDto(string MerchantCategoryCode, string Description, decimal Amount, int TransactionCount);

public record CashbackSummaryDto(
    int CardId,
    string MaskedCardNumber,
    decimal TotalEarned,
    decimal TotalReversed,
    decimal NetCashback,
    decimal ThisMonth,
    IReadOnlyList<CashbackCategoryTotalDto> ByCategory);

public record CashbackLogDto(
    int CashbackId,
    int TransactionId,
    int CardId,
    string MerchantName,
    string MerchantCategoryCode,
    decimal TransactionAmount,
    decimal CashbackPercentage,
    decimal CashbackAmount,
    string CashbackType,
    DateTime CreditedDate);
