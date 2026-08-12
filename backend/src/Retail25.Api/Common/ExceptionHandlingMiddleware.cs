using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Retail25.Application.Behaviors;

namespace Retail25.Api.Common;

/// <summary>
/// Converts the exceptions the pipeline throws into RFC 7807 responses.
/// <para>
/// Validation and authorisation are enforced as exceptions inside MediatR so the same rule applies
/// whether a request arrived over HTTP, over a hub or from a background job. This middleware is the
/// HTTP half of that: it renders them with the same <c>code</c> extension the domain errors use, so
/// the client has one error shape to handle.
/// </para>
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly RecentErrors _recent;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        RecentErrors recent)
    {
        _next = next;
        _logger = logger;
        _recent = recent;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, "validation.failed", "One or more fields are invalid.", new
            {
                errors = ex.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
            });
        }
        catch (PermissionDeniedException ex)
        {
            // A step-up-able action answers 428 so the till knows to ask for a supervisor rather
            // than telling the cashier they simply cannot do it (doc 05 error taxonomy).
            var status = ex.SupportsSupervisorApproval
                ? StatusCodes.Status428PreconditionRequired
                : StatusCodes.Status403Forbidden;

            var code = ex.SupportsSupervisorApproval ? "sale.requires_supervisor" : "permission.denied";

            await WriteAsync(context, status, code, ex.Message, new { permission = ex.Permission });
        }
        catch (Exception ex)
        {
            var traceId = TraceIdOf(context);

            _logger.LogError(ex, "Unhandled exception {TraceId} for {Path}", traceId, context.Request.Path);

            // Recorded as well as logged, because the log goes to a console this deployment has no
            // way to read back. The response still says nothing beyond the trace id — the detail is
            // behind audit.read, on /api/v1/diagnostics/errors.
            _recent.Record(
                DateTimeOffset.UtcNow,
                traceId,
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                ex);

            await WriteAsync(context, StatusCodes.Status500InternalServerError, "server.error", "An unexpected error occurred.", null);
        }
    }

    /// <summary>
    /// The id printed on the response and stamped on the recorded failure, so a shopkeeper reading
    /// an error off a till can be told exactly which one it was.
    /// </summary>
    private static string TraceIdOf(HttpContext context)
        => System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;

    private static async Task WriteAsync(HttpContext context, int status, string code, string detail, object? extensions)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail,
            Type = $"https://retail25.local/errors/{code}",
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = TraceIdOf(context);

        if (extensions is not null)
        {
            problem.Extensions["arguments"] = extensions;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
