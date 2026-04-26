using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models
{
    [Table("MASTER_FOLLOW_UP")]
    public class MasterFollowUp
    {
        [Key]
        [Column("FOLLOW_UP_ID")]
        public int FollowUpId { get; set; }

        [Column("REPORT_ID")]
        public int ReportId { get; set; }

        [Column("FOLLOW_UP_DATE")]
        public DateTime FollowUpDate { get; set; }

        [Column("RESPONSIBLE_PERSON")]
        public string ResponsiblePerson { get; set; } = string.Empty;

        [Column("MATERIALS_USED")]
        public string MaterialsUsed { get; set; } = string.Empty;

        [Column("DESCRIPTION")]
        public string Description { get; set; } = string.Empty;

        [Column("UPDATED_BY")]
        public int UpdatedBy { get; set; }
    }
}
