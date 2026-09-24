using SIGER.API.Middleware;

namespace SIGER.API.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseSigerApi(this WebApplication app)
    {
        // Logging wraps exception handling so it records the final mapped status.
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseStatusCodePages(async context =>
        {
            var http = context.HttpContext;
            await ApiProblems.WriteAsync(http, http.Response.StatusCode, "HTTP request failed.");
        });
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors("ConfiguredOrigins");
        app.UseRateLimiter();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "SIGER API v1");
                options.RoutePrefix = "swagger";
            });
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        return app;
    }
}
