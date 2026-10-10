using System.Globalization;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Application.Features.Notifications;

/// <summary>Category, title and text of one alert.</summary>
public sealed record Alert(NotificationCategory Category, string Title, string Message);

/// <summary>
/// The texts of every alert (Module 7), in one place - like a bank's SMS templates.
/// Cards appear only as "card ending 4057"; amounts as ₹1,500.00. No secrets, ever.
/// </summary>
public static class Alerts
{
    private const string NotYou = "Not you? Lock your card in the app.";

    public static string CardName(CreditCard card) => $"card ending {card.MaskedCardNumber[^4..]}";

    public static string Money(decimal amount) => "₹" + amount.ToString("N2", CultureInfo.InvariantCulture);

    // ---- Transactions -------------------------------------------------------------------------

    public static Alert PurchaseApproved(CreditCard card, CardTransaction t, decimal cashback, decimal cashFees = 0m)
    {
        if (t.IsCashWithdrawal)
            return new(NotificationCategory.Transaction, $"Cash withdrawal {Money(t.Amount)}",
                $"{Money(t.Amount)} withdrawn at {t.MerchantName} with {CardName(card)}. " +
                (cashFees > 0 ? $"Cash advance fee incl. GST {Money(cashFees)}; interest applies from today. " : "") +
                $"Available credit {Money(card.AvailableBalance)}. {NotYou}");

        var where = t.Channel is null ? "" : $" ({ChannelText(t)})";
        var reward = cashback > 0 ? $" Cashback {Money(cashback)}." : "";
        return new(NotificationCategory.Transaction, $"{Money(t.Amount)} spent at {t.MerchantName}",
            $"{Money(t.Amount)} spent at {t.MerchantName} with {CardName(card)}{where}.{reward} " +
            $"Available credit {Money(card.AvailableBalance)}. {NotYou}");
    }

    public static Alert PurchaseDeclined(CreditCard card, CardTransaction t) =>
        new(NotificationCategory.Transaction, "Payment declined",
            $"A payment of {Money(t.Amount)} at {t.MerchantName} with {CardName(card)} was declined: {t.DeclineReason}.");

    public static Alert RepaymentReceived(CreditCard card, decimal amount) =>
        new(NotificationCategory.Transaction, "Payment received",
            $"We received your payment of {Money(amount)} for {CardName(card)}. Available credit {Money(card.AvailableBalance)}.");

    public static Alert RefundCredited(CreditCard card, CardTransaction refund) =>
        new(NotificationCategory.Transaction, "Refund credited",
            $"A refund of {Money(refund.Amount)} from {refund.MerchantName} was credited to {CardName(card)}.");

    public static Alert ConvertedToEmi(CreditCard card, EmiPlan plan, CardTransaction purchase) =>
        new(NotificationCategory.Transaction, "Converted to EMI",
            $"Your purchase of {Money(purchase.Amount)} at {purchase.MerchantName} ({CardName(card)}) is now " +
            $"{plan.TenureMonths} monthly installments of {Money(plan.MonthlyInstallment)} at {plan.AnnualInterestRate:0.##}% p.a.");

    public static Alert EmiInstallmentPaid(CreditCard card, EmiPlan plan, int installmentNumber, decimal amount) =>
        new(NotificationCategory.Transaction, "EMI installment paid",
            $"Installment {installmentNumber} of {plan.TenureMonths} ({Money(amount)}) for " +
            $"{plan.Transaction?.MerchantName ?? "your EMI"} on {CardName(card)} was paid.");

    // ---- Card lifecycle ------------------------------------------------------------------------

    public static Alert CardIssued(CreditCard card) =>
        new(NotificationCategory.Card, "Your new card is ready",
            $"{Capitalize(CardName(card))} is active with a credit limit of {Money(card.CreditLimit)}. Online, contactless " +
            "and international use are off until you switch them on in Card controls.");

    public static Alert CardBlocked(CreditCard card) =>
        new(NotificationCategory.Card, "Card blocked",
            $"{Capitalize(CardName(card))} has been blocked. Only the bank can unblock it.");

    public static Alert CardUnblocked(CreditCard card) =>
        new(NotificationCategory.Card, "Card unblocked", $"The bank has unblocked {CardName(card)}.");

    public static Alert CreditLimitChanged(CreditCard card) =>
        new(NotificationCategory.Card, "Credit limit changed",
            $"The credit limit of {CardName(card)} is now {Money(card.CreditLimit)}.");

    // ---- Security ----------------------------------------------------------------------------

    public static Alert PinChanged(CreditCard card) =>
        new(NotificationCategory.Security, "PIN changed",
            $"The PIN of {CardName(card)} was changed. If this wasn't you, block the card immediately.");

    public static Alert CardNumberViewed(CreditCard card) =>
        new(NotificationCategory.Security, "Card number viewed",
            $"The full number of {CardName(card)} was shown in the app. If this wasn't you, block the card immediately.");

    public static Alert CardLocked(CreditCard card) =>
        new(NotificationCategory.Security, "Card locked",
            $"{Capitalize(CardName(card))} is locked. Purchases are declined until you unlock it.");

    public static Alert CardUnlocked(CreditCard card) =>
        new(NotificationCategory.Security, "Card unlocked",
            $"{Capitalize(CardName(card))} is unlocked and can be used again. If this wasn't you, lock it now.");

    public static Alert CardControlsChanged(CreditCard card, string summary) =>
        new(NotificationCategory.Security, "Card controls changed", $"New settings for {CardName(card)}: {summary}.");

    public static Alert SignedIn(DateTime atUtc) =>
        new(NotificationCategory.Security, "New sign-in",
            $"Your account was signed in on {atUtc.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)} UTC. " +
            "If this wasn't you, change your password and contact the bank.");

    // ---- Billing (Module 8) -------------------------------------------------------------------

    public static Alert StatementReady(CreditCard card, CardStatement s, Func<DateTime, string> date) =>
        s.ClosingBalance > 0
            ? new(NotificationCategory.Billing, $"Statement ready: {Money(s.ClosingBalance)} due by {date(s.DueDate)}",
                $"Your statement for {CardName(card)} is ready. Total due {Money(s.ClosingBalance)}, minimum due " +
                $"{Money(s.MinimumDue)}, pay by {date(s.DueDate)}. Pay the total to avoid interest.")
            : new(NotificationCategory.Billing, "Statement ready: nothing to pay",
                $"Your statement for {CardName(card)} is ready. There is nothing to pay this month.");

    public static Alert PaymentReminder(CreditCard card, CardStatement s, decimal minimumLeft, Func<DateTime, string> date) =>
        new(NotificationCategory.Billing, $"Payment due on {date(s.DueDate)}",
            $"Please pay at least {Money(minimumLeft)} on {CardName(card)} by {date(s.DueDate)} to avoid a late fee. " +
            $"Total due {Money(s.ClosingBalance)}.");

    public static Alert StatementOutcome(CreditCard card, CardStatement s, StatementAssessment outcome, decimal lateFee,
                                         decimal interest, decimal gst, Func<DateTime, string> date)
    {
        var charges = (lateFee > 0 ? $"Late fee {Money(lateFee)}" : "") +
                      (lateFee > 0 && interest > 0 ? " and " : "") +
                      (interest > 0 ? $"interest {Money(interest)}" : "") +
                      (gst > 0 ? $" + GST {Money(gst)}" : "");
        return outcome.Status == StatementStatus.Overdue
            ? new(NotificationCategory.Billing, "Payment overdue",
                $"The minimum due of {Money(s.MinimumDue)} on {CardName(card)} was not paid by {date(s.DueDate)} " +
                $"(paid {Money(outcome.PaidByDueDate)}). {(charges.Length > 0 ? charges + " charged." : "")}")
            : new(NotificationCategory.Billing, "Interest charged",
                $"You paid {Money(outcome.PaidByDueDate)} of the {Money(s.ClosingBalance)} due on {CardName(card)}. " +
                $"{(charges.Length > 0 ? Capitalize(charges) + " charged on the rest." : "")}");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static string ChannelText(CardTransaction t)
    {
        var channel = t.Channel switch
        {
            TransactionChannel.Online => "online",
            TransactionChannel.Contactless => "contactless",
            _ => "in store"
        };
        return t.IsInternational ? $"{channel}, abroad: {t.MerchantCountry}" : channel;
    }

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
