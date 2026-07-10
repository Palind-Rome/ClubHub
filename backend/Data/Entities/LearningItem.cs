using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("LEARNING_ITEMS")]
public class LearningItem
{
    [Column("ITEM_ID")]
    public int ItemId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("UPLOADER_USER_ID")]
    public int? UploaderUserId { get; set; }

    [Column("TEACHER_USER_ID")]
    public int? TeacherUserId { get; set; }

    [Column("TITLE")]
    public string Title { get; set; } = string.Empty;

    [Column("ITEM_TYPE")]
    public string? ItemType { get; set; }

    [Column("CATEGORY_NAME")]
    public string? CategoryName { get; set; }

    [Column("DESCRIPTION", TypeName = "CLOB")]
    public string? Description { get; set; }

    [Column("FILE_URL")]
    public string? FileUrl { get; set; }

    [Column("ENROLL_DEADLINE")]
    public DateTime EnrollmentDeadline { get; set; }

    [Column("START_AT")]
    public DateTime? StartAt { get; set; }

    [Column("END_AT")]
    public DateTime? EndAt { get; set; }

    [Column("CAPACITY")]
    public int? Capacity { get; set; }

    [Column("VISIBILITY")]
    public string? Visibility { get; set; }

    [Column("DOWNLOAD_PERMISSION")]
    public string? DownloadPermission { get; set; }

    [Column("ITEM_STATUS")]
    public string? ItemStatus { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(UploaderUserId))]
    public User? Uploader { get; set; }

    [ForeignKey(nameof(TeacherUserId))]
    public User? Teacher { get; set; }

    public ICollection<LearningRecord> Records { get; set; } = new List<LearningRecord>();
}
