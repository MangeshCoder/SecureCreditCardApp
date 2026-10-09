using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Api.InterBank;
using SecureEmiCard.Application.Features.Transactions;

namespace SecureEmiCard.Api.Controllers
{
    /// <summary>
    /// Server-to-server endpoint for partner banks (acquirers / payment gateways).
    /// No JWT: the caller is authenticated by InterBankSecurityMiddleware (HMAC signature, timestamp, nonce),
    /// which also decrypts the request before this controller runs and encrypts the response afterwards.
    /// It cannot be called from Swagger, because Swagger cannot sign and encrypt - use tools/PartnerBankSimulator.
    /// </summary>
    [ApiController]
    [Route("api/gateway/v1")]
    [AllowAnonymous]
    [RequireVerifiedPartner]
    public class GatewayController : ControllerBase
    {
        private readonly ITransactionService _transactions;

        public GatewayController(ITransactionService transactions) => _transactions = transactions;

        /// <summary>Authorizes a purchase sent by a partner bank. 200 for approved AND declined (see "approved").</summary>
        [HttpPost("authorize")]
        public async Task<ActionResult<SwipeResponse>> Authorize(SwipeRequest request, CancellationToken ct)
        {
            var partner = VerifiedPartner.From(HttpContext)!;
            var result = await _transactions.AuthorizeFromGatewayAsync(request, partner.RequestSignature, ct);

            HttpContext.Items[VerifiedPartner.AuditDetailKey] = result.Approved
                ? $"Approved, transaction #{result.TransactionId}"
                : $"Declined: {result.DeclineReason}" + (result.TransactionId is null ? string.Empty : $", transaction #{result.TransactionId}");
            return Ok(result);
        }
    }
}
