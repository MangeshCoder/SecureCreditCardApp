using SecureEmiCard.Application.Features.Cards;

namespace SecureEmiCard.Application.Features.Emi;

/// <summary>
/// EMI calculator input. Unlike the spec's EmiRequestDto there is no InterestRate:
/// the rate is decided by the bank per tenure, never by the client.
/// </summary>
public record EmiPreviewRequest(decimal PrincipalAmount, int TenureMonths);

/// <summary>Convert a purchase. (Spec: ConvertEmiDto without InterestRate, for the same reason.)</summary>
public record ConvertToEmiRequest(int TenureMonths);

public record EmiScheduleItemDto(
    int InstallmentNumber,
    DateOnly DueDate,
    decimal AmountDue,
    decimal PrincipalComponent,
    decimal InterestComponent,
    string PaymentStatus,
    DateTime? PaidDate,
    bool IsOverdue);

public record EmiCalculationDto(
    decimal PrincipalAmount,
    int TenureMonths,
    decimal AnnualInterestRate,
    decimal MonthlyInstallment,
    decimal TotalInterest,
    decimal TotalRepayable,
    IReadOnlyList<EmiScheduleItemDto> Schedule);

/// <summary>One row of the "choose your tenure" table.</summary>
public record EmiOptionDto(int TenureMonths, decimal AnnualInterestRate, decimal MonthlyInstallment,
                           decimal TotalInterest, decimal TotalRepayable);

public record EmiRulesDto(decimal MinimumAmount, int ConversionWindowDays, IReadOnlyList<EmiOptionRateDto> Rates);

public record EmiOptionRateDto(int TenureMonths, decimal AnnualInterestRate);

public record EligibleTransactionDto(int TransactionId, int CardId, string MerchantName, decimal Amount,
                                     DateTime TransactionDate, DateTime ConvertBeforeUtc);

public record EmiPlanDto(
    int EmiPlanId,
    int TransactionId,
    int CardId,
    string MerchantName,
    DateTime PurchaseDate,
    decimal PrincipalAmount,
    int TenureMonths,
    decimal AnnualInterestRate,
    decimal MonthlyInstallment,
    decimal TotalInterest,
    decimal TotalRepayable,
    decimal RemainingBalance,
    decimal OutstandingPrincipal,
    int PaidInstallments,
    string PlanStatus,
    DateTime CreatedDate,
    EmiScheduleItemDto? NextInstallment,
    IReadOnlyList<EmiScheduleItemDto> Schedule);

/// <summary>EMI position of a card - also tells "Pay bill" how much is NOT being repaid through EMIs.</summary>
public record CardEmiSummaryDto(
    int CardId,
    int ActivePlans,
    decimal OutstandingPrincipal,
    decimal RemainingBalance,
    decimal PayableOutsideEmi,
    DateOnly? NextDueDate,
    decimal? NextDueAmount);

public record PayInstallmentResponse(EmiPlanDto Plan, CardDto Card, int TransactionId);
