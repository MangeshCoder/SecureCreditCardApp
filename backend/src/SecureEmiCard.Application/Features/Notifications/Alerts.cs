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

    public static Alert PurchaseApproved(CreditCard card, CardTransaction t, decimal cashback)
    {
        if (t.IsCashWithdrawal)
            return new(NotificationCategory.Transaction, $"Cash withdrawal {Money(t.Amount)}",
                $"{Money(t.Amount)} withdrawn at {t.MerchantName} with {CardName(card)}. " +
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
