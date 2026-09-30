namespace SecureEmiCard.Application.Features.Cards;

public record CardDto(
    int CardId,
    int CardholderId,
    string MaskedCardNumber,
    decimal CreditLimit,
    decimal AvailableBalance,
    decimal OutstandingAmount,
    string CardStatus,
    DateOnly ExpiryDate,
    DateTime CreatedAt);

/// <summary>Admin request to issue a new card to an existing cardholder.</summary>
public record IssueCardRequest(int CardholderId, decimal CreditLimit);

/// <summary>
/// Returned exactly once, at issuance (like a physical "PIN mailer").
/// The full card number, CVV and initial PIN are never returned by any other endpoint:
/// CVV and PIN are only stored as hashes, so they cannot be recovered afterwards.
/// </summary>
public record IssuedCardResponse(CardDto Card, string CardNumber, string Cvv, string InitialPin);

public record UpdateCreditLimitRequest(decimal NewCreditLimit);

public record ChangePinRequest(string CurrentPin, string NewPin);

public record RevealCardNumberRequest(string Pin);

public record RevealCardNumberResponse(int CardId, string CardNumber);
