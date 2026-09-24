using SIGER.API.Extensions;
using SIGER.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
builder.Services.AddSigerApi(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSigerApi();
app.Run();

public partial class Program;
