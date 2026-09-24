using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using SIGER.API.Authorization;
using SIGER.API.Extensions;
using SIGER.API.Middleware;

namespace SIGER.API.Tests;

public class ConfigurationAndLoggingTests
{
    private static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["ConnectionStrings:SIGERDatabase"] = "Host=127.0.0.1;Port=1;Database=never_connect",
        ["Supabase:Url"] = "https://test.invalid", ["Supabase:ServiceRoleKey"] = "synthetic-test-only"
    };

    [Theory]
    [InlineData("ConnectionStrings:SIGERDatabase", null)]
    [InlineData("Supabase:Url", null)] [InlineData("Supabase:ServiceRoleKey", null)]
    [InlineData("Supabase:Url", "http://test.invalid")]
    [InlineData("Supabase:Url", "https://user:input@test.invalid")]
    [InlineData("Supabase:JwtIssuer", "http://test.invalid")]
    [InlineData("Cors:AllowedOrigins:0", "*")]
    [InlineData("Cors:AllowedOrigins:0", "https://test.invalid/path")]
    public void Startup_rejects_missing_or_unsafe_configuration(string key, string? value)
    {
        var values = ValidConfiguration(); values[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSigerApi(config));
        Assert.DoesNotContain("synthetic-test-only", error.Message);
        Assert.DoesNotContain("user:input", error.Message);
    }

    [Fact]
    public void Jwt_validation_uses_asymmetric_JWKS_with_rotation_manager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSigerApi(new ConfigurationBuilder().AddInMemoryCollection(ValidConfiguration()).Build());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var validation = options.TokenValidationParameters;
        Assert.True(validation.ValidateIssuer); Assert.True(validation.ValidateAudience);
        Assert.True(validation.ValidateLifetime); Assert.True(validation.ValidateIssuerSigningKey);
        Assert.True(validation.RequireSignedTokens); Assert.True(options.RequireHttpsMetadata);
        Assert.Equal(["RS256", "ES256"], validation.ValidAlgorithms);
        Assert.Null(validation.IssuerSigningKey);
        Assert.False(options.SaveToken); Assert.False(options.IncludeErrorDetails);
        Assert.True(options.RefreshOnIssuerKeyNotFound);
        var manager = Assert.IsType<ConfigurationManager<OpenIdConnectConfiguration>>(options.ConfigurationManager);
        Assert.Equal("https://test.invalid/auth/v1/.well-known/jwks.json", manager.MetadataAddress);
    }

    [Fact]
    public async Task Jwks_retriever_accepts_multiple_rotation_keys_without_network()
    {
        using var first = RSA.Create(2048); using var second = RSA.Create(2048);
        var one = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(first) { KeyId = "old" });
        var two = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(second) { KeyId = "new" });
        var document = System.Text.Json.JsonSerializer.Serialize(new { keys = new[]
        {
            new { kty = one.Kty, kid = one.Kid, n = one.N, e = one.E, use = "sig" },
            new { kty = two.Kty, kid = two.Kid, n = two.N, e = two.E, use = "sig" }
        } });
        var retriever = new Mock<IDocumentRetriever>();
        using var cts = new CancellationTokenSource();
        retriever.Setup(x => x.GetDocumentAsync("https://test.invalid/jwks", cts.Token)).ReturnsAsync(document);
        var config = await new SupabaseJwksRetriever("https://test.invalid/auth/v1")
            .GetConfigurationAsync("https://test.invalid/jwks", retriever.Object, cts.Token);
        Assert.Equal("https://test.invalid/auth/v1", config.Issuer);
        Assert.Equal(["new", "old"], config.SigningKeys.Select(x => x.KeyId).Order());
        retriever.VerifyAll();
    }

    [Fact]
    public async Task Request_logging_excludes_headers_query_and_body()
    {
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST"; context.Request.Path = "/api/v1/auth/login";
        context.Request.QueryString = new("?token=PRIVATE_QUERY");
        context.Request.Headers.Authorization = "Bearer PRIVATE_AUTH";
        context.Request.Headers.Cookie = "PRIVATE_COOKIE";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"password\":\"PRIVATE_BODY\"}"));
        await new RequestLoggingMiddleware(_ => { context.Response.StatusCode = 401; return Task.CompletedTask; }, logger).InvokeAsync(context);
        var log = Assert.Single(logger.Messages);
        Assert.Contains("/api/v1/auth/login", log); Assert.Contains("401", log);
        Assert.DoesNotContain("PRIVATE", log); Assert.DoesNotContain("Bearer", log);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public async Task Provider_exception_is_logged_only_in_development_and_never_exposed_in_response(
        string environmentName, bool logsException)
    {
        var logger = new RecordingLogger<ExceptionHandlingMiddleware>();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(environmentName);
        var exception = new PostgresException("column c.activo does not exist", "ERROR", "ERROR", "42703");
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "diagnostic-test-trace";
        using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        context.RequestServices = services;
        context.Request.Path = "/api/v1/products/available";
        context.Response.Body = new MemoryStream();

        await new ExceptionHandlingMiddleware(_ => Task.FromException(exception), logger, environment.Object)
            .InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var recordedException = Assert.Single(logger.Exceptions);
        if (logsException) Assert.Same(exception, recordedException);
        else Assert.Null(recordedException);
        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
        Assert.Contains(context.TraceIdentifier, Assert.Single(logger.Messages));
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("Unexpected error.", body);
        Assert.Contains(context.TraceIdentifier, body);
        Assert.DoesNotContain("activo", body);
        Assert.DoesNotContain("42703", body);
        Assert.DoesNotContain("PostgresException", body);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, error));
            Exceptions.Add(error);
            Levels.Add(level);
        }
    }
}
