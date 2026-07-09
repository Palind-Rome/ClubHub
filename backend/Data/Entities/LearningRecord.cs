using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("LEARNING_RECORDS")]
public class LearningRecord
{
    [Column("RECORD_ID")]
    public int RecordId { get; set; }

    [Column("ITEM_ID")]
    public int ItemId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("ENROLL_STATUS")]
    public string? EnrollStatus { get; set; }

    [Column("ENROLLED_AT")]
    public DateTime? EnrolledAt { get; set; }

    [Column("PROGRESS")]
    public decimal? Progress { get; set; }

    [Column("DURATION_SECONDS")]
    public int? DurationSeconds { get; set; }

    [Column("LAST_LEARN_AT")]
    public DateTime? LastLearnAt { get; set; }

    [Column("COMPLETED_AT")]
    public DateTime? CompletedAt { get; set; }

    [Column("DOWNLOADED_AT")]
    public DateTime? DownloadedAt { get; set; }

    [Column("DOWNLOAD_IP")]
    public string? DownloadIp { get; set; }

    [ForeignKey(nameof(ItemId))]
    public LearningItem? Item { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
