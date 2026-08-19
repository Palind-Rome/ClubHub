using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClubHub.Api.Infrastructure.Redis;

internal static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var response = new
        {
            status = FormatStatus(report.Status),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = FormatStatus(entry.Value.Status),
                    description = entry.Value.Description
                })
        };

        return context.Response.WriteAsJsonAsync(response);
    }

    private static string FormatStatus(HealthStatus status) =>
        status.ToString().ToLowerInvariant();
}
