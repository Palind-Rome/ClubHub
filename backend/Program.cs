using ClubHub.Api.Data;
using ClubHub.Api.Services;
using ClubHub.Api.Validation;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Org.OpenAPITools.Converters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.TypeInfoResolverChain.Insert(0, new DefaultJsonTypeInfoResolver
        {
            Modifiers = { GeneratedJsonRequiredMembers.Apply }
        });
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumMemberConverter());
    });
builder.Services.AddSingleton<AuthTokenService>();
builder.Services.Configure<OssStorageOptions>(
    builder.Configuration.GetSection(OssStorageOptions.SectionName));
builder.Services.AddSingleton<ILearningObjectStorage, OssLearningObjectStorage>();
builder.Services.AddSingleton<IAwardObjectStorage, OssAwardObjectStorage>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RecruitmentApplicationService>();
builder.Services.AddScoped<ProjectMembershipService>();
builder.Services
    .AddAuthentication(AuthTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, AuthTokenAuthenticationHandler>(
        AuthTokenAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();

builder.Services.AddDbContext<ClubHubDbContext>(options =>
    options.UseOracle(builder.Configuration.GetConnectionString("Default"))
);

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.InitializeBaseRolesAsync();
}

app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

public partial class Program { }
