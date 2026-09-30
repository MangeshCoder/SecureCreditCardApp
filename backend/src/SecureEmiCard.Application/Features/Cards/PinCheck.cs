using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Cards;

public enum PinCheckResult
{
    Valid,
    Invalid,
    /// <summary>Wrong PIN and this was the last allowed attempt - the card is now blocked.</summary>
    LockedOut
}

/// <summary>
/// Verifies a PIN and applies the lockout rule (3 wrong PINs in a row block the card).
/// Every PIN check in the system goes through here: swipe, change PIN and reveal number.
/// The caller must save changes, including after a failure, so the attempt counter is persisted.
/// </summary>
public static class PinCheck
{
    public static PinCheckResult Verify(ISecretHasher hasher, CreditCard card, string pin)
    {
        if (hasher.Verify(pin, card.PinHash))
        {
            if (card.FailedPinAttempts > 0) card.ResetFailedPinAttempts();
            return PinCheckResult.Valid;
        }

        card.RegisterFailedPinAttempt();
        return card.IsActive ? PinCheckResult.Invalid : PinCheckResult.LockedOut;
    }
}
