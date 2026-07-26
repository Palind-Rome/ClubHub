using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("IDEMPOTENCY_RECORDS")]
public sealed class IdempotencyRecord
{
    [Column("IDEMPOTENCY_ID")]
    public int IdempotencyId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("OPERATION_SCOPE")]
    public string OperationScope { get; set; } = string.Empty;

    [Column("REQUEST_KEY_HASH")]
    public string RequestKeyHash { get; set; } = string.Empty;

    [Column("REQUEST_HASH")]
    public string RequestHash { get; set; } = string.Empty;

    [Column("RECORD_STATUS")]
    public string RecordStatus { get; set; } = string.Empty;

    [Column("HTTP_STATUS")]
    public int? HttpStatus { get; set; }

    [Column("CONTENT_TYPE")]
    public string? ContentType { get; set; }

    [Column("RESPONSE_HEADERS")]
    public string? ResponseHeaders { get; set; }

    [Column("RESPONSE_BODY")]
    public string? ResponseBody { get; set; }

    [Column("EXPIRES_AT")]
    public DateTime ExpiresAt { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [Column("UPDATED_AT")]
    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }
}
