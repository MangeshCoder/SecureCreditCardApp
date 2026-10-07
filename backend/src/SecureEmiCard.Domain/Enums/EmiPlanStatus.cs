namespace SecureEmiCard.Domain.Enums;

/// <summary>Stored as NVARCHAR in EmiPlans.PlanStatus.</summary>
public enum EmiPlanStatus
{
    Active,
    /// <summary>All installments paid.</summary>
    Closed
}
