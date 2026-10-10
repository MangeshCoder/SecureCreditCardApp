using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Domain.Common
{
    /// <summary>Values of SecurityAuditLogs.ActionType. One place, so reports and filters use the same names.</summary>
    public static class AuditActions
    {
        // Inter-bank gateway (Module 5)
        public const string GatewayAuthorize = "GatewayAuthorize";
        // Authentication
        public const string Login = "Login";
        public const string Register = "Register";
        // Card lifecycle and secrets
        public const string CardIssued = "CardIssued";
        public const string CardBlocked = "CardBlocked";
        public const string CardUnblocked = "CardUnblocked";
        public const string CreditLimitChanged = "CreditLimitChanged";
        public const string PinChanged = "PinChanged";
        public const string CardNumberRevealed = "CardNumberRevealed";
        // Card controls (Module 6)
        public const string CardLocked = "CardLocked";
        public const string CardUnlocked = "CardUnlocked";
        public const string CardControlsChanged = "CardControlsChanged";

        // Customers
        public const string CardholderActivated = "CardholderActivated";
        public const string CardholderDeactivated = "CardholderDeactivated";

        // Money movements by the back office / customer
        public const string Refund = "Refund";
        public const string EmiConversion = "EmiConversion";
        public const string StatementGenerated = "StatementGenerated";

        public static readonly IReadOnlyList<string> All = new[]
        {
            GatewayAuthorize, Login, Register, CardIssued, CardBlocked, CardUnblocked, CreditLimitChanged,
            PinChanged, CardNumberRevealed, CardLocked, CardUnlocked, CardControlsChanged,
            CardholderActivated, CardholderDeactivated, Refund, EmiConversion, StatementGenerated
        };
    }
}
