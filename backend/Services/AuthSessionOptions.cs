namespace ClubHub.Api.Services;

public sealed class AuthSessionOptions
{
    public const string SectionName = "Authentication:Sessions";

    public int SlidingLifetimeMinutes { get; init; } = 30;

    public int AbsoluteLifetimeHours { get; init; } = 12;

    public int MaxSessionsPerUser { get; init; } = 10;
}
