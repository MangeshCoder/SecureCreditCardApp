using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SecureEmiCard.Api.InterBank
{
    /// <summary>Placed in HttpContext.Items by the middleware once signature, timestamp and nonce are verified.</summary>
    public sealed record VerifiedPartner(string PartnerId, string Name, string RequestSignature, string RequestNonce)
    {
        public const string ItemKey = "InterBank.VerifiedPartner";
        /// <summary>Optional text the controller can leave for the audit row (e.g. "Approved #12").</summary>
        public const string AuditDetailKey = "InterBank.AuditDetail";

        public static VerifiedPartner? From(HttpContext context) =>
            context.Items.TryGetValue(ItemKey, out var value) ? value as VerifiedPartner : null;
    }

    /// <summary>
    /// Defence in depth: even if the middleware were accidentally removed from Program.cs, gateway actions
    /// still refuse to run without a verified partner.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequireVerifiedPartnerAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (VerifiedPartner.From(context.HttpContext) is null)
                context.Result = new UnauthorizedResult();
        }
    }
}
