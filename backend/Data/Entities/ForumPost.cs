using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("FORUM_POSTS")]
public class ForumPost
{
    [Column("POST_ID")]
    public int PostId { get; set; }

    [Column("CLUB_ID")]
    public int ClubId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("PARENT_POST_ID")]
    public int? ParentPostId { get; set; }

    [Column("TITLE")]
    public string? Title { get; set; }

    [Column("CONTENT")]
    public string Content { get; set; } = string.Empty;

    [Column("IS_TOP")]
    public int IsTop { get; set; }

    [Column("POST_STATUS")]
    public string PostStatus { get; set; } = "published";

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime? UpdatedAt { get; set; }

    public Club? Club { get; set; }

    public User? User { get; set; }

    public ForumPost? ParentPost { get; set; }

    public ICollection<ForumPost> Replies { get; set; } = new List<ForumPost>();
}
