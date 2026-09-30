using FluentValidation;

namespace SecureEmiCard.Application.Features.Cards;

public static class CardLimits
{
    public const decimal MaxCreditLimit = 1_000_000m;
}

public class IssueCardRequestValidator : AbstractValidator<IssueCardRequest>
{
    public IssueCardRequestValidator()
    {
        RuleFor(x => x.CardholderId).GreaterThan(0);
        RuleFor(x => x.CreditLimit).GreaterThan(0).LessThanOrEqualTo(CardLimits.MaxCreditLimit)
            .PrecisionScale(18, 2, true);
    }
}

public class UpdateCreditLimitRequestValidator : AbstractValidator<UpdateCreditLimitRequest>
{
    public UpdateCreditLimitRequestValidator()
    {
        RuleFor(x => x.NewCreditLimit).GreaterThan(0).LessThanOrEqualTo(CardLimits.MaxCreditLimit)
            .PrecisionScale(18, 2, true);
    }
}

public class ChangePinRequestValidator : AbstractValidator<ChangePinRequest>
{
    public ChangePinRequestValidator()
    {
        RuleFor(x => x.CurrentPin).Must(PinPolicy.IsWellFormed).WithMessage("Current PIN must be exactly 4 digits.");
        RuleFor(x => x.NewPin)
            .Must(PinPolicy.IsWellFormed).WithMessage("New PIN must be exactly 4 digits.")
            .Must(pin => !PinPolicy.IsWeak(pin)).WithMessage("New PIN is too easy to guess (e.g. 1111 or 1234).")
            .When(x => PinPolicy.IsWellFormed(x.NewPin), ApplyConditionTo.CurrentValidator)
            .NotEqual(x => x.CurrentPin).WithMessage("New PIN must be different from the current PIN.");
    }
}

public class RevealCardNumberRequestValidator : AbstractValidator<RevealCardNumberRequest>
{
    public RevealCardNumberRequestValidator()
    {
        RuleFor(x => x.Pin).Must(PinPolicy.IsWellFormed).WithMessage("PIN must be exactly 4 digits.");
    }
}
