using FluentValidation;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Transactions;

public static class TransactionLimits
{
    public const decimal MaxAmount = 1_000_000m;
}

public class SwipeRequestValidator : AbstractValidator<SwipeRequest>
{
    public SwipeRequestValidator()
    {
        RuleFor(x => x.CardNumber)
            .NotEmpty()
            .Matches(@"^\d{16}$").WithMessage("Card number must be 16 digits.")
            .Must(CardNumberGenerator.IsValidLuhn).WithMessage("Card number is not valid.");
        RuleFor(x => x.ExpiryMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.ExpiryYear).InclusiveBetween(2000, 2100).WithMessage("Expiry year must have 4 digits.");
        RuleFor(x => x.Cvv).Matches(@"^\d{3}$").WithMessage("CVV must be 3 digits.");
        RuleFor(x => x.Pin).Must(PinPolicy.IsWellFormed).WithMessage("PIN must be exactly 4 digits.");
        RuleFor(x => x.MerchantName).NotEmpty().MaximumLength(CardTransaction.MaxMerchantNameLength);
        RuleFor(x => x.MerchantCategoryCode).Matches(@"^\d{4}$").WithMessage("Merchant category code must be 4 digits.");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(TransactionLimits.MaxAmount).PrecisionScale(18, 2, true);
    }
}

public class LoadRequestValidator : AbstractValidator<LoadRequest>
{
    public LoadRequestValidator()
    {
        RuleFor(x => x.CardId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(TransactionLimits.MaxAmount).PrecisionScale(18, 2, true);
    }
}
