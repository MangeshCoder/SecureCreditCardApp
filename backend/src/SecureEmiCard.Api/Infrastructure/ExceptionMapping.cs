using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Domain.Common;
using FluentValidation;

namespace SecureEmiCard.Api.Infrastructure
{
    /// <summary>
    /// The ONE place that decides which HTTP status and message an exception becomes.
    /// Used by the global exception handler, the inter-bank middleware (to encrypt error responses)
    /// and the audit filter (to record the right status) - so all three always agree.
    /// </summary>
    public static class ExceptionMapping
    {
        public static ProblemDetails ToProblemDetails(Exception exception) => exception switch
        {
            ValidationException ve => new ValidationProblemDetails(
                ve.Errors.GroupBy(e => ToCamelCase(e.PropertyName))
                         .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            DomainException => Problem(StatusCodes.Status400BadRequest, "Business rule violated", exception.Message),
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Not found", exception.Message),
            ConcurrencyConflictException => Problem(StatusCodes.Status409Conflict, "Conflict", exception.Message),
            ConflictException => Problem(StatusCodes.Status409Conflict, "Conflict", exception.Message),
            UnauthorizedException => Problem(StatusCodes.Status401Unauthorized, "Unauthorized", exception.Message),
            ForbiddenException => Problem(StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
            // Anything unexpected: generic message - stack traces and internals never reach the client.
            _ => Problem(StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.")
        };

        public static int StatusCodeFor(Exception exception) => ToProblemDetails(exception).Status!.Value;

        private static ProblemDetails Problem(int status, string title, string detail) =>
            new() { Status = status, Title = title, Detail = detail };

        private static string ToCamelCase(string name) =>
            string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
