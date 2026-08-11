using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models;

[Table("MASTER_REPORTER")]
public sealed class MasterReporter
{
    [Key]
    [Column("REPORTER_ID")]
    public int ReporterId { get; set; }

    [Required]
    [Column("REPORTER_NAME")]
    public string ReporterName { get; set; } = string.Empty;

    [Required]
    [Column("FULLNAME")]
    public string Fullname { get; set; } = string.Empty;

    [Column("CREATED_DATE")]
    public DateTime CreatedDate { get; set; }

    [Column("CREATED_ID")]
    public int CreatedBy { get; set; }

    [Column("IS_ACTIVE")]
    public bool IsActive { get; set; } = true;
}
