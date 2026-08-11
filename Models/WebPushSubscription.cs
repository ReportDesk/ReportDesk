using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models;

[Table("WEB_PUSH_SUBSCRIPTION")]
public sealed class WebPushSubscription
{
    [Key]
    [Column("SUBSCRIPTION_ID")]
    public int SubscriptionId { get; set; }

    [Column("USER_ID")]
    public int UserId { get; set; }

    [Column("ENDPOINT")]
    public string Endpoint { get; set; } = string.Empty;

    [Column("P256DH")]
    public string P256dh { get; set; } = string.Empty;

    [Column("AUTH")]
    public string Auth { get; set; } = string.Empty;

    [Column("CREATED_DATE")]
    public DateTime CreatedDate { get; set; }

    [Column("UPDATED_DATE")]
    public DateTime UpdatedDate { get; set; }
}
