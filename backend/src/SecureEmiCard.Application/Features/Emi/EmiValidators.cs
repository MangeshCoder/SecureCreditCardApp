using FluentValidation;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Emi;

public class EmiPreviewRequestValidator : AbstractValidator<EmiPreviewRequest>
{
    public EmiPreviewRequestValidator()
    {
        RuleFor(x => x.PrincipalAmount).GreaterThan(0).LessThanOrEqualTo(TransactionLimits.MaxAmount).PrecisionScale(18, 2, true);
        RuleFor(x => x.TenureMonths).Must(t => EmiPlan.AllowedTenures.Contains(t))
            .WithMessage("Tenure must be 3, 6, 12 or 24 months.");
    }
}

public class ConvertToEmiRequestValidator : AbstractValidator<ConvertToEmiRequest>
{
    public ConvertToEmiRequestValidator()
    {
        RuleFor(x => x.TenureMonths).Must(t => EmiPlan.AllowedTenures.Contains(t))
            .WithMessage("Tenure must be 3, 6, 12 or 24 months.");
    }
}
