using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("AWARD_ATTACHMENTS")]
public class AwardAttachment
{
    [Column("ATTACHMENT_ID")]
    public int AttachmentId { get; set; }

    [Column("AWARD_APPLICATION_ID")]
    public int AwardApplicationId { get; set; }

    [Column("ATTACHMENT_NAME")]
    public string AttachmentName { get; set; } = string.Empty;

    [Column("ATTACHMENT_URL")]
    public string AttachmentUrl { get; set; } = string.Empty;

    [Column("ATTACHMENT_TYPE")]
    public string? AttachmentType { get; set; }

    [Column("UPLOADED_BY_USER_ID")]
    public int UploadedByUserId { get; set; }

    [Column("UPLOADED_AT")]
    public DateTime UploadedAt { get; set; }

    [ForeignKey(nameof(AwardApplicationId))]
    public AwardApplication? Application { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User? UploadedByUser { get; set; }
}
