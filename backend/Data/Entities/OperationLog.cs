using System.ComponentModel.DataAnnotations.Schema;

namespace ClubHub.Api.Data.Entities;

[Table("OPERATION_LOGS")]
public class OperationLog
{
    [Column("LOG_ID")]
    public int LogId { get; set; }

    [Column("USER_ID")]
    public int? UserId { get; set; }

    [Column("MODULE_NAME")]
    public string? ModuleName { get; set; }

    [Column("OPERATION_TYPE")]
    public string? OperationType { get; set; }

    [Column("TARGET_TABLE")]
    public string? TargetTable { get; set; }

    [Column("TARGET_ID")]
    public int? TargetId { get; set; }

    [Column("IP_ADDRESS")]
    public string? IpAddress { get; set; }

    [Column("CREATED_AT")]
    public DateTime CreatedAt { get; set; }
}
