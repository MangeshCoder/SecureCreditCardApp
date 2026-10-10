namespace SecureEmiCard.Application.Features.Billing;

public record StatementDto(
    int StatementId,
    int CardId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime DueDate,
    decimal OpeningBalance,
    decimal Purchases,
    decimal CashWithdrawals,
    decimal FeesAndCharges,
    decimal Payments,
    decimal Refunds,
    decimal Cashback,
    decimal MovedToEmi,
    decimal ClosingBalance,
    decimal MinimumDue,
    decimal EmiInstallmentsDue,
    string Status,
    decimal? PaidByDueDate);

/// <summary>
/// One line of a statement. Amount is signed like the balance: + increases what you owe (purchase, charge),
/// − reduces it (payment, refund, cashback, moved to EMI). The lines add up to Closing − Opening.
/// </summary>
public record StatementLineDto(DateTime Date, string Description, string Kind, decimal Amount);

public record LateFeeSlabDto(decimal? UpTo, decimal Fee);

/// <summary>The bank's billing terms, printed on every statement.</summary>
public record BillingRulesDto(
    decimal MinimumDuePercent,
    decimal MinimumDueFloor,
    decimal MonthlyInterestPercent,
    decimal CashAdvanceFeePercent,
    decimal CashAdvanceMinimumFee,
    decimal GstPercent,
    int PaymentDueDays,
    IReadOnlyList<LateFeeSlabDto> LateFees);

public record StatementDetailDto(
    StatementDto Statement,
    string CardholderName,
    string MaskedCardNumber,
    decimal CreditLimit,
    IReadOnlyList<StatementLineDto> Lines,
    BillingRulesDto Rules,
    TimeSpan UtcOffset); // the bank's time zone (IST): dates on the PDF are printed in it

/// <summary>"Your bill" right now: the last statement and what is still to pay on it.</summary>
public record CardBillingSummaryDto(
    int CardId,
    StatementDto? LastStatement,
    decimal PaidSinceStatement,
    decimal RemainingMinimumDue,
    decimal RemainingTotalDue,
    bool IsOverdue,
    decimal UnbilledAmount,
    DateTime NextStatementDate);

public record StatementPdf(string FileName, byte[] Content);

/// <summary>What one scheduler run did.</summary>
public record BillingRunResult(int StatementsGenerated, int StatementsAssessed, int RemindersSent, int Failures);
