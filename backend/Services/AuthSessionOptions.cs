using System.ComponentModel.DataAnnotations;

namespace ClubHub.Api.Services;

public sealed class AuthSessionOptions
{
    public const string SectionName = "Authentication:Sessions";

    [Range(1, 24 * 60)]
    public int SlidingLifetimeMinutes { get; init; } = 30;

    [Range(1, 24 * 365)]
    public int AbsoluteLifetimeHours { get; init; } = 12;

    [Range(1, 1000)]
    public int MaxSessionsPerUser { get; init; } = 10;
}
