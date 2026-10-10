using SecureEmiCard.Application.Abstractions.Security;

namespace SecureEmiCard.Api.Infrastructure;

/// <summary>
/// Module 7: reads the one-time code from the request headers
/// <c>X-Otp-Challenge-Id</c> and <c>X-Otp-Code</c>. Malformed values count as "a wrong code was sent"
/// (challenge id 0 never exists), so they fail instead of silently triggering a new SMS.
/// </summary>
public class HttpOtpProofAccessor : IOtpProofAccessor
{
    public const string ChallengeHeader = "X-Otp-Challenge-Id";
    public const string CodeHeader = "X-Otp-Code";

    private readonly IHttpContextAccessor _http;

    public HttpOtpProofAccessor(IHttpContextAccessor http) => _http = http;

    public OtpProof? Current
    {
        get
        {
            var headers = _http.HttpContext?.Request.Headers;
            if (headers is null || (!headers.ContainsKey(ChallengeHeader) && !headers.ContainsKey(CodeHeader)))
                return null;

            var id = int.TryParse(headers[ChallengeHeader].ToString(), out var parsed) && parsed > 0 ? parsed : 0;
            var code = headers[CodeHeader].ToString().Trim();
            return new OtpProof(id, code.Length <= 10 ? code : code[..10]);
        }
    }
}
