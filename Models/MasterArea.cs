using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models;

[Table("MASTER_AREA")]
public sealed class MasterArea
{
    [Key]
    [Column("AREA_ID")]
    public int AreaId { get; set; }

    [Required]
    [Column("AREA_NAME")]
    public string AreaName { get; set; } = string.Empty;

    [Column("CREATED_DATE")]
    public DateTime CreatedDate { get; set; }

    [Column("CREATED_ID")]
    public int CreatedBy { get; set; }

    [Column("IS_ACTIVE")]
    public bool IsActive { get; set; } = true;
}
