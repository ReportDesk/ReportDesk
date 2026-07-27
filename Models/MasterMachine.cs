using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models
{
    [Table("MASTER_MACHINE")]
    public class MasterMachine
    {
        [Key]
        [Column("MACHINE_ID")]
        public int MachineId { get; set; }

        [Column("MACHINE_NAME")]
        public string MachineName { get; set; } = string.Empty;

        [Column("AREA")]
        public string MachineArea { get; set; }

        [Column("TYPE")]
        public string MachineType { get; set; }

        [Column("CREATED_DATE")]
        public DateTime CreatedDate { get; set; }

        [Column("CREATED_ID")]
        public int CreatedBy { get; set; }

        [Column("IS_ACTIVE")]
        public bool IsActive { get; set; } = true;
    }
}
