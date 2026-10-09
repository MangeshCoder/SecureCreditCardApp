using Microsoft.Extensions.Options;
using SecureEmiCard.Api.Infrastructure;
using SecureEmiCard.Application.Abstractions.Auditing;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecureEmiCard.Api.InterBank
{
    /// <summary>
    /// End-to-end inter-bank payload security (spec §6), runs for every request under /api/gateway:
    ///
    ///   1. required headers present            -> else 401
    ///   2. partner known and enabled           -> else 401
    ///   3. timestamp within ±5 minutes         -> else 401   (old captured requests are useless)
    ///   4. body not larger than the limit      -> else 413
    ///   5. HMAC signature valid (constant time)-> else 401   (nothing was tampered with)
    ///   6. nonce never used before             -> else 401   (exact replays are useless)
    ///   7. decrypt the AES payload             -> else 400
    ///   8. hand the PLAIN JSON to the controller, then ENCRYPT + SIGN the controller's response
    ///   9. write a SecurityAuditLogs row for every request - accepted or rejected
    ///
    /// Signature BEFORE nonce: an attacker without the key cannot use up a partner's nonces.
    /// Signature BEFORE decryption: we never decrypt (or reveal decryption errors for) forged data.
    /// All authentication failures return the same generic 401 message; the precise reason is only in the
    /// audit log, so an attacker learns nothing about which check failed.
    /// </summary>
    public class InterBankSecurityMiddleware
    {
        private const string GenericAuthError = "Request authentication failed.";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly RequestDelegate _next;
        private readonly ILogger<InterBankSecurityMiddleware> _logger;

        public InterBankSecurityMiddleware(RequestDelegate next, ILogger<InterBankSecurityMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, PartnerRegistry partners, INonceStore nonces,
                                      ISignatureService signatures, IPayloadCryptoService crypto,
                                      IAuditLogWriter audit, IOptions<InterBankOptions> options)
        {
            var opt = options.Value;
            var request = context.Request;
            var headers = request.Headers;
            string partnerId = headers[InterBankProtocol.PartnerIdHeader].ToString();
            string timestamp = headers[InterBankProtocol.TimestampHeader].ToString();
            string nonce = headers[InterBankProtocol.NonceHeader].ToString();
            string signature = headers[InterBankProtocol.SignatureHeader].ToString();

            // Helper: reject, audit, stop. PartnerId is only recorded once it is known to be a real partner.
            async Task Reject(int status, string publicMessage, string auditReason, string payloadHash,
                              bool signatureValid = false, string? knownPartner = null)
            {
                _logger.LogWarning("Inter-bank request rejected ({Status}): {Reason}, partner header '{Partner}'",
                    status, auditReason, partnerId);
                await WriteAuditAsync(audit, context, AuditOutcome.Rejected, status, signatureValid, payloadHash,
                                      knownPartner, auditReason);
                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new { error = publicMessage });
            }

            // 1. Headers
            if (!HttpMethods.IsPost(request.Method))
            {
                await Reject(StatusCodes.Status405MethodNotAllowed, "Only POST is supported.", "Method not allowed", "-");
                return;
            }
            if (partnerId.Length == 0 || timestamp.Length == 0 || nonce.Length == 0 || signature.Length == 0)
            {
                await Reject(StatusCodes.Status401Unauthorized, GenericAuthError, "Missing security headers", "-");
                return;
            }

            // 2. Partner
            var partner = partners.FindActive(partnerId);
            if (partner is null)
            {
                await Reject(StatusCodes.Status401Unauthorized, GenericAuthError, $"Unknown or disabled partner '{partnerId}'", "-");
                return;
            }

            // 3. Freshness
            if (!long.TryParse(timestamp, out var unixSeconds) ||
                Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixSeconds) > opt.AllowedClockSkewSeconds)
            {
                await Reject(StatusCodes.Status401Unauthorized, GenericAuthError, "Timestamp outside the allowed window", "-",
                             knownPartner: partner.PartnerId);
                return;
            }
            if (!InterBankProtocol.IsValidNonce(nonce))
            {
                await Reject(StatusCodes.Status401Unauthorized, GenericAuthError, "Malformed nonce", "-", knownPartner: partner.PartnerId);
                return;
            }

            // 4. Body (bounded read - never trust Content-Length alone)
            var body = await ReadBodyAsync(request, opt.MaxBodyBytes);
            if (body is null)
            {
                await Reject(StatusCodes.Status413PayloadTooLarge, "Payload too large.", "Body larger than the limit", "-",
                             knownPartner: partner.PartnerId);
                return;
            }
            string payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

            // 5. Signature (constant-time comparison inside Verify)
            string canonical = InterBankProtocol.RequestCanonical(request.Method, request.Path.Value ?? string.Empty,
                                                                   timestamp, nonce, body);
            if (!signatures.Verify(canonical, signature, partner.SigningKey))
            {
                await Reject(StatusCodes.Status401Unauthorized, GenericAuthError, "Invalid signature", payloadHash,
                             knownPartner: partner.PartnerId);
                return;
            }

            // 6. Replay
            if (!nonces.TryUse(partner.PartnerId, nonce, TimeSpan.FromSeconds(opt.NonceTtlSeconds)))
            {
                await Reject(StatusCodes.Status401Unauthorized, GenericAuthError, "Replayed request (nonce already used)",
                             payloadHash, signatureValid: true, knownPartner: partner.PartnerId);
                return;
            }

            // 7. Decrypt (only authentic messages get here)
            string plainJson;
            try
            {
                var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(body, Json);
                if (string.IsNullOrEmpty(envelope?.Payload)) throw new FormatException("Empty payload.");
                plainJson = crypto.Decrypt(envelope.Payload, partner.EncryptionKey);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
            {
                await Reject(StatusCodes.Status400BadRequest, "Payload could not be decrypted.", "Decryption failed",
                             payloadHash, signatureValid: true, knownPartner: partner.PartnerId);
                return;
            }

            // 8. Run the controller with the plain JSON, capturing its response so it can be encrypted.
            context.Items[VerifiedPartner.ItemKey] = new VerifiedPartner(partner.PartnerId, partner.Name, signature, nonce);
            var plainBytes = Encoding.UTF8.GetBytes(plainJson);
            request.Body = new MemoryStream(plainBytes);
            request.ContentLength = plainBytes.Length;
            request.ContentType = "application/json";

            var originalBody = context.Response.Body;
            using var captured = new MemoryStream();
            context.Response.Body = captured;
            string? errorDetail = null;
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Errors are answered INSIDE the secure channel too (encrypted + signed below), so the partner
                // can trust them - the same rules as the global handler decide the status and message.
                var problem = ExceptionMapping.ToProblemDetails(ex);
                if (problem.Status >= 500) _logger.LogError(ex, "Gateway request failed for partner {Partner}", partner.PartnerId);
                captured.SetLength(0);
                context.Response.StatusCode = problem.Status!.Value;
                await JsonSerializer.SerializeAsync(captured, problem, problem.GetType(), Json);
                errorDetail = $"{ex.GetType().Name}: {problem.Detail ?? problem.Title}";
            }
            finally
            {
                context.Response.Body = originalBody;
            }

            var responseJson = Encoding.UTF8.GetString(captured.ToArray());
            var status = context.Response.StatusCode;
            var responseBody = JsonSerializer.Serialize(
                new EncryptedEnvelope(crypto.Encrypt(responseJson, partner.EncryptionKey)), Json);
            var responseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var responseNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            context.Response.Headers[InterBankProtocol.TimestampHeader] = responseTimestamp;
            context.Response.Headers[InterBankProtocol.NonceHeader] = responseNonce;
            context.Response.Headers[InterBankProtocol.SignatureHeader] = signatures.Sign(
                InterBankProtocol.ResponseCanonical(status, responseTimestamp, responseNonce, nonce, responseBody),
                partner.SigningKey);
            context.Response.ContentType = "application/json";
            var responseBytes = Encoding.UTF8.GetBytes(responseBody);
            context.Response.ContentLength = responseBytes.Length;
            await context.Response.Body.WriteAsync(responseBytes);

            // 9. Audit the accepted request
            context.Items.TryGetValue(VerifiedPartner.AuditDetailKey, out var detail);
            await WriteAuditAsync(audit, context, status is >= 200 and < 300 ? AuditOutcome.Success : AuditOutcome.Failed,
                                  status, true, payloadHash, partner.PartnerId, errorDetail ?? detail as string);
        }

        /// <summary>Reads at most maxBytes; returns null if the body is larger.</summary>
        private static async Task<string?> ReadBodyAsync(HttpRequest request, int maxBytes)
        {
            if (request.ContentLength > maxBytes) return null;
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await request.Body.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > maxBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static Task WriteAuditAsync(IAuditLogWriter audit, HttpContext context, AuditOutcome outcome, int status,
                                            bool signatureValid, string payloadHash, string? partnerId, string? detail) =>
            audit.WriteAsync(new AuditEntry(
                AuditActions.GatewayAuthorize,
                $"{context.Request.Method} {context.Request.Path}",
                outcome, signatureValid, payloadHash, status,
                UserId: null,
                PartnerId: partnerId,
                ClientIp: context.Connection.RemoteIpAddress?.ToString(),
                CorrelationId: context.TraceIdentifier,
                Detail: detail));
    }
}
