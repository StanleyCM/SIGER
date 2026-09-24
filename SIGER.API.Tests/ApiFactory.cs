using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;
using SIGER.Application.Base;
using SIGER.Application.Interfaces.Repositories;
using SIGER.Application.Interfaces.Services;
using SIGER.Domain.Entities;

namespace SIGER.API.Tests;

internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<Type, Mock> mocks = new();
    private readonly RSA rsa = RSA.Create(2048);
    private readonly ECDsa ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public const string Issuer = "https://test.invalid/auth/v1";
    public User LocalUser { get; } = new() { Id = 42, AuthUserId = Guid.NewGuid(), IsActive = true,
        FirstName = "Test", LastName = "User", Role = new Role { Name = "Administrator", IsActive = true } };
    public bool UserExists { get; set; } = true;
    public int LoginLimit { get; set; } = 100;
    public int PublicLimit { get; set; } = 100;
    public string EnvironmentName { get; set; } = "Development";
    public Mock<IUserRepository> Users { get; } = new();
    public Mock<T> Service<T>() where T : class => (Mock<T>)mocks[typeof(T)];

    public ApiFactory()
    {
        foreach (var type in typeof(IAuthService).Assembly.GetTypes().Where(t => t.IsInterface &&
                     t.Namespace == "SIGER.Application.Interfaces.Services"))
        {
            var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;
            mock.DefaultValueProvider = new SuccessfulResults();
            mocks[type] = mock;
        }
        Users.Setup(x => x.GetByAuthUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => UserExists && id == LocalUser.AuthUserId ? LocalUser : null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SIGERDatabase"] = "Host=127.0.0.1;Port=1;Database=never_connect;Username=test",
            ["Supabase:Url"] = "https://test.invalid",
            ["Supabase:ServiceRoleKey"] = "synthetic-test-only",
            ["Supabase:JwtIssuer"] = Issuer,
            ["Supabase:JwtAudience"] = "authenticated",
            ["Cors:AllowedOrigins:0"] = "https://allowed.example.test",
            ["RateLimiting:LoginPermitLimit"] = LoginLimit.ToString(),
            ["RateLimiting:PublicPermitLimit"] = PublicLimit.ToString()
        };
        foreach (var pair in values) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values));
        builder.ConfigureTestServices(services =>
        {
            foreach (var pair in mocks)
            {
                services.RemoveAll(pair.Key);
                services.AddSingleton(pair.Key, pair.Value.Object);
            }
            // Every repository is strict and disconnected. An accidental call fails locally.
            foreach (var type in typeof(IUserRepository).Assembly.GetTypes().Where(t =>
                         t.IsInterface && t.Namespace == "SIGER.Application.Interfaces.Repositories"))
            {
                services.RemoveAll(type);
                var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type), MockBehavior.Strict)!;
                services.AddSingleton(type, mock.Object);
            }
            services.RemoveAll<IUserRepository>(); services.AddSingleton(Users.Object);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(new RsaSecurityKey(rsa) { KeyId = "test-rsa" });
                configuration.SigningKeys.Add(new ECDsaSecurityKey(ec) { KeyId = "test-ec" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }

    public HttpClient Client(string? role = null)
    {
        LocalUser.Role.Name = role ?? "Administrator";
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        return client;
    }

    public string Token(string? issuer = null, string audience = "authenticated", bool expired = false,
        bool wrongSignature = false, bool ecdsa = false, string? subject = null, bool spoofRole = false)
    {
        using var other = wrongSignature ? RSA.Create(2048) : null;
        SecurityKey key = ecdsa ? new ECDsaSecurityKey(ec) { KeyId = "test-ec" } :
            new RsaSecurityKey(other ?? rsa) { KeyId = "test-rsa" };
        var claims = new List<Claim> { new("sub", subject ?? LocalUser.AuthUserId.ToString()) };
        if (spoofRole) { claims.Add(new("siger_role", "Administrator")); claims.Add(new("role", "Administrator")); claims.Add(new("siger_user_id", "999")); }
        var jwt = new JwtSecurityToken(issuer ?? Issuer, audience, claims, DateTime.UtcNow.AddHours(-2),
            expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(key, ecdsa ? SecurityAlgorithms.EcdsaSha256 : SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { rsa.Dispose(); ec.Dispose(); } }

    private sealed class SuccessfulResults : DefaultValueProvider
    {
        protected override object GetDefaultValue(Type type, Mock mock) => Create(type)!;
        private static object? Create(Type type)
        {
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type == typeof(Result)) return Result.Success();
            if (type.IsGenericType)
            {
                var generic = type.GetGenericTypeDefinition(); var arg = type.GetGenericArguments()[0];
                if (generic == typeof(Task<>)) return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(arg).Invoke(null, [Create(arg)]);
                if (generic == typeof(Result<>)) return type.GetMethod("Success", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.Invoke(null, [Create(arg)]);
                if (generic == typeof(PaginatedResult<>)) return Activator.CreateInstance(type, Array.CreateInstance(arg, 0), 0, 1, 20);
                if (generic == typeof(IReadOnlyCollection<>)) return Array.CreateInstance(arg, 0);
            }
            if (type.IsClass && type.GetConstructor(Type.EmptyTypes) is not null) return Activator.CreateInstance(type);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
