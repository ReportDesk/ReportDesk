using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models
{
    [Table("MASTER_REPORT")]
    public class MasterReport
    {
        [Key]
        [Column("REPORT_ID")]
        public int ReportId { get; set; }

        [Column("REPORT_TYPE")]
        public string ReportType { get; set; } // Add this to the Database table

        [Column("TITLE")]
        public string ReportTitle { get; set; }

        [Column("REPORTED_NAME")]
        public string ReportedBy { get; set; }

        [Column("DESCRIPTION")]
        public string Description { get; set; }

        [Column("SUBMISSION_DATE")]
        public DateTime SubmissionDate { get; set; }

        [Column("AREA")]
        public string Area { get; set; }

        [Column("STATUS")]
        public string Status { get; set; }

        [Column("UPDATED_DATE")]
        public DateTime UpdatedDate { get; set; }

        [Column("UPDATED_BY")] // Fetch user ID from MASTER_USER table. Only for when the report is updated
        public int UpdatedBy { get; set; }

        [Column("MACHINE_ID")] // fetch Machine ID from MASTER_MACHINE table
        public int MachineId { get; set; }
    }
}
