using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SIGER.API.Authorization;
using SIGER.API.Authorization.Policies;
using SIGER.API.Configuration;
using SIGER.Application.Interfaces.Services;
using SIGER.Application.Services;

namespace SIGER.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSigerApi(this IServiceCollection services, IConfiguration configuration)
    {
        var supabase = configuration.GetSection(SupabaseSettings.SectionName).Get<SupabaseSettings>() ?? new();
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("SIGERDatabase"))) missing.Add("ConnectionStrings:SIGERDatabase");
        if (string.IsNullOrWhiteSpace(supabase.Url)) missing.Add("Supabase:Url");
        if (string.IsNullOrWhiteSpace(supabase.ServiceRoleKey)) missing.Add("Supabase:ServiceRoleKey");
        if (missing.Count > 0)
            throw new InvalidOperationException($"Missing runtime configuration: {string.Join(", ", missing)}. Configure API User Secrets or environment variables.");

        if (!Uri.TryCreate(supabase.Url, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            throw new InvalidOperationException("Supabase:Url must be an absolute HTTPS project URL without credentials, query or fragment.");

        var issuer = string.IsNullOrWhiteSpace(supabase.JwtIssuer) ? supabase.Url.TrimEnd('/') + "/auth/v1" : supabase.JwtIssuer.TrimEnd('/');
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) || issuerUri.Scheme != "https" ||
            !string.IsNullOrEmpty(issuerUri.UserInfo) || !string.IsNullOrEmpty(issuerUri.Query) || !string.IsNullOrEmpty(issuerUri.Fragment))
            throw new InvalidOperationException("Supabase:JwtIssuer must be an absolute HTTPS issuer URL.");
        var jwt = new JwtSettings
        {
            Issuer = issuer,
            Audience = string.IsNullOrWhiteSpace(supabase.JwtAudience) ? "authenticated" : supabase.JwtAudience,
            JwksUrl = supabase.Url.TrimEnd('/') + "/auth/v1/.well-known/jwks.json"
        };
        services.Configure<SupabaseSettings>(configuration.GetSection(SupabaseSettings.SectionName));
        services.AddSingleton(jwt);
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.AllowInputFormatterExceptionMessages = false;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
                options.JsonSerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter());
            });
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ITableService, TableService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IKitchenService, KitchenService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAuditService, AuditService>();

        services.AddScoped<SigerJwtBearerEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.SaveToken = false;
            options.IncludeErrorDetails = false;
            options.RequireHttpsMetadata = true;
            options.Authority = jwt.Issuer;
            options.Audience = jwt.Audience;
            options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                jwt.JwksUrl, new SupabaseJwksRetriever(jwt.Issuer),
                new HttpDocumentRetriever { RequireHttps = true });
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                ValidateAudience = true, ValidAudience = jwt.Audience,
                ValidateLifetime = true, RequireExpirationTime = true,
                ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
                RoleClaimType = SigerClaims.Role
            };
            options.EventsType = typeof(SigerJwtBearerEvents);
        });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(ApiPolicies.AdministratorOnly, policy => policy.RequireAuthenticatedUser().RequireRole(SigerClaims.Administrator));
            options.AddPolicy(ApiPolicies.StaffOnly, policy => policy.RequireAuthenticatedUser().RequireRole(SigerClaims.Administrator, SigerClaims.Waiter, SigerClaims.Cook, SigerClaims.Cashier));
            options.AddPolicy(ApiPolicies.WaiterOrAdministrator, policy => policy.RequireAuthenticatedUser().RequireRole(SigerClaims.Administrator, SigerClaims.Waiter));
            options.AddPolicy(ApiPolicies.KitchenOrAdministrator, policy => policy.RequireAuthenticatedUser().RequireRole(SigerClaims.Administrator, SigerClaims.Cook));
            options.AddPolicy(ApiPolicies.CashierOrAdministrator, policy => policy.RequireAuthenticatedUser().RequireRole(SigerClaims.Administrator, SigerClaims.Cashier));
            options.AddPolicy(ApiPolicies.Reservations, policy => policy.RequireAuthenticatedUser().RequireRole(SigerClaims.Administrator, SigerClaims.Waiter, SigerClaims.Client));
        });
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => origin == "*" || !Uri.TryCreate(origin, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != "http" && parsed.Scheme != "https") || parsed.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.UserInfo)))
            throw new InvalidOperationException("Cors:AllowedOrigins must contain explicit HTTP/HTTPS origins without paths or credentials.");
        services.AddCors(options => options.AddPolicy("ConfiguredOrigins", policy =>
        {
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await ApiProblems.WriteAsync(context.HttpContext, 429, "Too many requests.", "Retry after the rate limit window.");
            };
            options.AddPolicy("Login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, configuration.GetValue("RateLimiting:LoginPermitLimit", 5)),
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
            options.AddPolicy("Public", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, configuration.GetValue("RateLimiting:PublicPermitLimit", 60)),
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
        });
        services.AddOpenApi("v1", options =>
        {
            options.AddOperationTransformer((operation, context, token) =>
            {
                if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                    operation.Security = [];
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer((document, context, token) =>
            {
                document.Info.Title = "SIGER API";
                document.Info.Version = "v1";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                    Description = "Supabase access token; SIGER roles are resolved from the local user profile."
                };
                foreach (var path in document.Paths.Values)
                foreach (var operation in path.Operations?.Values.AsEnumerable() ?? [])
                    operation.Security ??= [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
