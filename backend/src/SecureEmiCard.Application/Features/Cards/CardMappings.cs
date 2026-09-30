using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Cards;

public static class CardMappings
{
    public static CardDto ToDto(this CreditCard c) =>
        new(c.CardId, c.CardholderId, c.MaskedCardNumber, c.CreditLimit, c.AvailableBalance,
            c.OutstandingAmount, c.CardStatus.ToString(), c.ExpiryDate, c.CreatedAt);
}
