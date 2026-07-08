using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("NOTICES")]
public class Notice
{
    [Column("NOTICE_ID")]
    public int NoticeId { get; set; }

    [Column("CLUB_ID")]
    public int? ClubId { get; set; }

    [Column("PUBLISHER_USER_ID")]
    public int PublisherUserId { get; set; }

    [Column("NOTICE_TYPE")]
    public string? NoticeType { get; set; }

    [Column("TITLE")]
    public string Title { get; set; } = string.Empty;

    [Column("CONTENT")]
    public string Content { get; set; } = string.Empty;

    [Column("TARGET_TYPE")]
    public string TargetType { get; set; } = string.Empty;

    [Column("TARGET_ID")]
    public int? TargetId { get; set; }

    [Column("PUBLISH_AT")]
    public DateTime PublishAt { get; set; }

    [Column("EXPIRE_AT")]
    public DateTime? ExpireAt { get; set; }

    [Column("NOTICE_STATUS")]
    public string? NoticeStatus { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [ForeignKey(nameof(PublisherUserId))]
    public User? Publisher { get; set; }

    public ICollection<NoticeRead> Reads { get; set; } = new List<NoticeRead>();
}
