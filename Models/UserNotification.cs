using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models;

[Table("USER_NOTIFICATION")]
public sealed class UserNotification
{
    [Key]
    [Column("NOTIFICATION_ID")]
    public int NotificationId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("REPORT_ID")]
    public int ReportId { get; set; }

    [Column("TITLE")]
    public string Title { get; set; } = string.Empty;

    [Column("MESSAGE")]
    public string Message { get; set; } = string.Empty;

    [Column("PRIORITY")]
    public string Priority { get; set; } = "Normal";

    [Column("REPORT_URL")]
    public string ReportUrl { get; set; } = string.Empty;

    [Column("IS_READ")]
    public bool IsRead { get; set; }

    [Column("READ_DATE")]
    public DateTime? ReadDate { get; set; }

    [Column("ACKNOWLEDGED_DATE")]
    public DateTime? AcknowledgedDate { get; set; }

    [Column("ACKNOWLEDGED_BY")]
    public int? AcknowledgedBy { get; set; }

    [Column("CREATED_DATE")]
    public DateTime CreatedDate { get; set; }
}
