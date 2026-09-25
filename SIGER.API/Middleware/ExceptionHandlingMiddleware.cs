using Microsoft.EntityFrameworkCore;
using Npgsql;
using SIGER.API.Extensions;
using SIGER.Application.Exceptions;
using SIGER.Domain.Exceptions;

namespace SIGER.API.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var (status, title, detail) = exception switch
            {
                ValidationException => (400, "Validation failed.", "One or more values are invalid."),
                UnauthorizedException => (401, "Authentication required.", "Authentication could not be verified."),
                NotFoundException => (404, "Resource not found.", "The requested resource does not exist."),
                BusinessRuleException => (409, "Business rule conflict.", "The operation conflicts with the current state."),
                DomainException => (400, "Invalid operation.", "The operation does not satisfy the domain rules."),
                DbUpdateConcurrencyException => (409, "Concurrency conflict.", "The record changed. Reload it and try again."),
                UserOperationException operation => (500, "User operation failed.", $"The operation could not be completed safely. Operation reference: {operation.OperationId:D}."),
                DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "23503" } } =>
                    (409, "Data conflict.", "A duplicate or related record prevents this operation."),
                BadHttpRequestException => (400, "Invalid request.", "The request could not be read."),
                _ => (500, "Unexpected error.", "An unexpected error occurred. Use the traceId when contacting support.")
            };
            if (environment.IsDevelopment())
            {
                // Keep provider diagnostics in local logs, never in the HTTP response.
                if (status >= 500)
                    logger.LogError(exception, "Request failed. TraceId: {TraceId}", context.TraceIdentifier);
                else
                    logger.LogWarning(exception, "Request failed. TraceId: {TraceId}", context.TraceIdentifier);
            }
            else
            {
                logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning,
                    "Request failed with {ExceptionType}; trace {TraceId}", exception.GetType().Name, context.TraceIdentifier);
            }
            context.Response.Clear();
            await ApiProblems.WriteAsync(context, status, title, detail);
        }
    }
}
