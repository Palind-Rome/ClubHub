namespace ClubHub.Api.Data;

public static class RecruitmentStatuses
{
    public const string Draft = "draft";
    public const string PendingReview = "pending_review";
    public const string Published = "published";
    public const string Closed = "closed";
    public const string NotStarted = "not_started";
    public const string Accepting = "accepting";
    public const string Ended = "ended";

    public static readonly IReadOnlySet<string> WorkflowStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Draft,
            PendingReview
        };

    public static readonly IReadOnlySet<string> StorageStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Draft,
            PendingReview,
            Published,
            Closed
        };

    public static readonly IReadOnlySet<string> EffectiveStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Draft,
            PendingReview,
            NotStarted,
            Accepting,
            Ended
        };
}
