using Microsoft.AspNetCore.Mvc;

namespace SIGER.API.Extensions;

public static class ApiProblems
{
    public static async Task WriteAsync(HttpContext context, int status, string title, string? detail = null,
        IDictionary<string, string[]>? errors = null)
    {
        var problem = errors is null
            ? new ProblemDetails()
            : new ValidationProblemDetails(errors);
        problem.Status = status;
        problem.Title = title;
        problem.Detail = detail;
        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = status;
        var service = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        if (!await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
        {
            await context.Response.WriteAsJsonAsync(problem, options: null,
                contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}
