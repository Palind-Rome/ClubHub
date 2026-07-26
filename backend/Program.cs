using ClubHub.Api.Data;
using ClubHub.Api.Infrastructure.Redis;
using ClubHub.Api.Infrastructure.Idempotency;
using ClubHub.Api.Services;
using ClubHub.Api.Validation;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services
    .AddOptions<AuthSessionOptions>()
    .Bind(builder.Configuration.GetSection(AuthSessionOptions.SectionName));
builder.Services.AddSingleton<IAuthSessionService, AuthSessionService>();
builder.Services.AddSingleton<IPermissionSnapshotCache, PermissionSnapshotCache>();
builder.Services.AddSingleton<IDistributedRateLimiter, DistributedRateLimiter>();
builder.Services.AddHostedService<IdempotencyCleanupService>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});
builder.Services.Configure<OssStorageOptions>(
    builder.Configuration.GetSection(OssStorageOptions.SectionName));
builder.Services.Configure<LearningPreviewOptions>(
    builder.Configuration.GetSection(LearningPreviewOptions.SectionName));
builder.Services.AddSingleton<ILearningObjectStorage, OssLearningObjectStorage>();
builder.Services.AddSingleton<IAwardObjectStorage, OssAwardObjectStorage>();
builder.Services.AddSingleton<OfficePreviewConverter>();
builder.Services.AddSingleton<OfficeConversionLimiter>();
builder.Services.AddSingleton<LearningPreviewService>();
builder.Services.AddSingleton<LearningPreviewSessionStore>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RecruitmentApplicationService>();
builder.Services.AddScoped<ProjectMembershipService>();
builder.Services.AddScoped<PublicQueryCacheService>();
builder.Services
    .AddAuthentication(AuthTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, AuthTokenAuthenticationHandler>(
        AuthTokenAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddClubHubRedis(builder.Configuration);

builder.Services.AddScoped<PermissionInvalidationInterceptor>();
builder.Services.AddDbContext<ClubHubDbContext>((services, options) =>
    options
        .UseOracle(builder.Configuration.GetConnectionString("Default"))
        .AddInterceptors(services.GetRequiredService<PermissionInvalidationInterceptor>()));

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.InitializeBaseRolesAsync();
}

app.UseForwardedHeaders();

app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (PermissionSnapshotUnavailableException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "无法安全失效权限缓存，本次写入已回滚，请稍后重试。"
        });
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live"),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync
    });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync
    });
app.MapControllers();
app.Run();

public partial class Program { }
