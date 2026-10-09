using Microsoft.AspNetCore.Diagnostics;

namespace SecureEmiCard.Api.Infrastructure;

/// <summary>
/// Converts exceptions into RFC 7807 ProblemDetails responses.
/// The status/message rules live in <see cref="ExceptionMapping"/> (shared with the gateway and the audit filter).
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetails;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    {
        _logger = logger;
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = ExceptionMapping.ToProblemDetails(exception);

        if (problem.Status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            _logger.LogInformation("Request {Method} {Path} failed with {Status}: {Message}",
                context.Request.Method, context.Request.Path, problem.Status, exception.Message);

        context.Response.StatusCode = problem.Status!.Value;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}