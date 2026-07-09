using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("NOTICE_READS")]
public class NoticeRead
{
    [Column("READ_ID")]
    public int ReadId { get; set; }

    [Column("NOTICE_ID")]
    public int NoticeId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("READ_AT")]
    public DateTime ReadAt { get; set; }

    [ForeignKey(nameof(NoticeId))]
    public Notice? Notice { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
