using ClubHub.Api.Data;
using ClubHub.Api.Infrastructure.Redis;
using ClubHub.Api.Infrastructure.Idempotency;
using ClubHub.Api.Infrastructure.Rest;
using ClubHub.Api.Services;
using ClubHub.Api.Validation;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Org.OpenAPITools.Converters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    {
        options.Conventions.Insert(0, new ApiVersionRouteConvention());
        options.Filters.Add<ApiErrorResultFilter>();
    })
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
    .Bind(builder.Configuration.GetSection(AuthSessionOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options =>
            TimeSpan.FromHours(options.AbsoluteLifetimeHours) >=
            TimeSpan.FromMinutes(options.SlidingLifetimeMinutes),
        "Authentication session absolute lifetime must not be shorter than sliding lifetime.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CaptchaService>();
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
builder.Services.AddScoped<PermissionTransactionInterceptor>();
builder.Services.AddScoped<PermissionInvalidationCoordinator>();
builder.Services.AddDbContext<ClubHubDbContext>((services, options) =>
    options
        .UseOracle(builder.Configuration.GetConnectionString("Default"))
        .AddInterceptors(
            services.GetRequiredService<PermissionInvalidationInterceptor>(),
            services.GetRequiredService<PermissionTransactionInterceptor>()));

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.InitializeBaseRolesAsync();
}

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    if (ApiRouteDeprecation.IsDeprecated(context.Request.Path))
    {
        context.Response.Headers["Deprecation"] = "true";
        context.Response.Headers["Sunset"] = "Thu, 31 Dec 2026 16:00:00 GMT";
        if (ApiRouteDeprecation.TryGetSuccessor(context.Request.Path, out var successorPath))
        {
            var querySeparator = successorPath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var successorQuery = context.Request.QueryString.HasValue
                ? $"{querySeparator}{context.Request.QueryString.Value![1..]}"
                : string.Empty;
            context.Response.Headers.Link =
                $"<{successorPath}{successorQuery}>; rel=\"successor-version\"";
        }
    }

    await next();
});

app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());

app.UseRouting();

app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    var request = statusCodeContext.HttpContext.Request;
    if (request.Path.StartsWithSegments("/api") && response.StatusCode >= 400)
    {
        await response.WriteAsJsonAsync(ApiErrorFactory.Create(response.StatusCode));
    }
});

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (PermissionSnapshotUnavailableException) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(ApiErrorFactory.Create(
            StatusCodes.Status503ServiceUnavailable,
            "无法安全失效权限缓存，本次写入已回滚，请稍后重试。"));
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
