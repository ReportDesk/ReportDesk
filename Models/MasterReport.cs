using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection.PortableExecutable;

namespace ReportSystem.Models
{
    [Table("MASTER_REPORT")]
    public class MasterReport
    {
        [Key]
        [Column("REPORT_ID")]
        public int ReportId { get; set; }

        [Column("REPORT_TYPE")]
        public string ReportType { get; set; } = string.Empty;

        [Column("TITLE")]
        public string ReportTitle { get; set; } = string.Empty;

        [Column("REPORTED_NAME")]
        public string ReportedBy { get; set; } = string.Empty;

        [Column("DESCRIPTION")]
        public string Description { get; set; } = string.Empty;

        [Column("SUBMISSION_DATE")]
        public DateTime SubmissionDate { get; set; }

        [Column("AREA")]
        public string Area { get; set; } = string.Empty;

        [Column("STATUS")]
        public string Status { get; set; } = string.Empty;

        [Column("UPDATED_DATE")]
        public DateTime UpdatedDate { get; set; }

        [Column("UPDATED_BY")]
        public int UpdatedBy { get; set; }

        [Column("MACHINE_ID")]
        public int? MachineId { get; set; }

        [ForeignKey("MachineId")]
        public MasterMachine? Machine { get; set; }
    }
}
