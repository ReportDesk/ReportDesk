using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models;

[Table("PENDING_R2_OBJECT_DELETION")]
public class PendingR2ObjectDeletion
{
    [Key]
    [Column("PENDING_R2_OBJECT_DELETION_ID")]
    public int PendingR2ObjectDeletionId { get; set; }

    [Required]
    [MaxLength(1024)]
    [Column("OBJECT_KEY")]
    public string ObjectKey { get; set; } = string.Empty;

    [Column("RECORDED_AT")]
    public DateTime RecordedAt { get; set; }

    [Column("ATTEMPT_COUNT")]
    public int AttemptCount { get; set; }

    [Column("LAST_ATTEMPT_AT")]
    public DateTime? LastAttemptAt { get; set; }
}
