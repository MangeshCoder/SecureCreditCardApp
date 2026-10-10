using FluentValidation;
using SecureEmiCard.Application.Features.Cards;

namespace SecureEmiCard.Application.Features.CardControls;

public class ChannelSettingRequestValidator : AbstractValidator<ChannelSettingRequest>
{
    public ChannelSettingRequestValidator()
    {
        // The upper bound per card (the credit limit) is checked by the domain, which knows the card.
        RuleFor(x => x.DailyLimit!.Value)
            .GreaterThan(0).LessThanOrEqualTo(CardLimits.MaxCreditLimit).PrecisionScale(18, 2, true)
            .OverridePropertyName("DailyLimit")
            .When(x => x.DailyLimit is not null);
    }
}

public class UpdateCardControlsRequestValidator : AbstractValidator<UpdateCardControlsRequest>
{
    public UpdateCardControlsRequestValidator()
    {
        var channel = new ChannelSettingRequestValidator();
        RuleFor(x => x.Pos).NotNull().SetValidator(channel);
        RuleFor(x => x.Online).NotNull().SetValidator(channel);
        RuleFor(x => x.Contactless).NotNull().SetValidator(channel);
        RuleFor(x => x.Atm).NotNull().SetValidator(channel);
        RuleFor(x => x.International).NotNull().SetValidator(channel);
    }
}
