using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using SecureEmiCard.Api.Infrastructure;
using SecureEmiCard.Application.Abstractions.Auditing;
using SecureEmiCard.Application.Features.Auth;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Api.Auditing;

/// <summary>
/// Declarative auditing: put <c>[Audit(AuditActions.CardBlocked)]</c> on a controller action and every call
/// - successful, rejected or failed - is written to SecurityAuditLogs. No audit code inside the services.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditAttribute : TypeFilterAttribute
{
    public AuditAttribute(string actionType) : base(typeof(AuditActionFilter))
    {
        Arguments = new object[] { actionType };
    }
}

/// <summary>Lets an action add a short summary of what it changed to its audit row (never secrets).</summary>
public static class AuditDetail
{
    private const string ItemKey = "SecureEmiCard.AuditDetail";

    public static void SetAuditDetail(this HttpContext http, string detail) => http.Items[ItemKey] = detail;

    internal static string? GetAuditDetail(this HttpContext http) =>
        http.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
}

public class AuditActionFilter : IAsyncActionFilter
{
    private readonly string _actionType;
    private readonly IAuditLogWriter _audit;

    public AuditActionFilter(string actionType, IAuditLogWriter audit)
    {
        _actionType = actionType;
        _audit = audit;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        var http = context.HttpContext;

        int status;
        string? detail;
        if (executed.Exception is { } ex && !executed.ExceptionHandled)
        {
            status = ExceptionMapping.StatusCodeFor(ex);
            // Business messages only ("Incorrect PIN. 2 attempt(s) left") - never request values.
            detail = status >= 500 ? ex.GetType().Name : ex.Message;
        }
        else
        {
            status = executed.Result switch
            {
                IStatusCodeActionResult { StatusCode: { } code } => code,
                _ => http.Response.StatusCode
            };
            detail = http.GetAuditDetail(); // e.g. Module 6: "Online on (limit 20000.00) ..."
        }

        var outcome = status switch
        {
            >= 200 and < 300 => AuditOutcome.Success,
            428 => AuditOutcome.Challenged, // Module 7: a one-time code was sent; the repeated request decides
            401 or 403 or 429 => AuditOutcome.Rejected,
            _ => AuditOutcome.Failed
        };

        // Who: the JWT subject; for register/login the new/logged-in user comes from the response body.
        int? userId = int.TryParse(http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
        if (userId is null && executed.Result is ObjectResult { Value: AuthResponse auth })
            userId = auth.User.CardholderId;

        // What: route values such as "cardId=5" identify the object without exposing request bodies.
        var target = string.Join(", ", context.RouteData.Values
            .Where(v => v.Key is not ("action" or "controller"))
            .Select(v => $"{v.Key}={v.Value}"));
        detail = string.Join(" | ", new[] { target, detail }.Where(s => !string.IsNullOrEmpty(s)));

        await _audit.WriteAsync(new AuditEntry(
            _actionType,
            $"{http.Request.Method} {http.Request.Path}",
            outcome,
            SignatureValid: http.User.Identity?.IsAuthenticated == true, // JWT signature was validated
            PayloadHash: RequestFingerprint(http, userId),
            HttpStatus: status,
            UserId: userId,
            ClientIp: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier,
            Detail: detail.Length == 0 ? null : detail));
    }

    /// <summary>
    /// SHA-256 of method, path, user and correlation id. The request BODY is deliberately not hashed:
    /// it can contain a PIN or CVV, and with only 10,000 possible PINs a hash of it could be reversed.
    /// </summary>
    private static string RequestFingerprint(HttpContext http, int? userId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{http.Request.Method}|{http.Request.Path}{http.Request.QueryString}|{userId}|{http.TraceIdentifier}")));
}