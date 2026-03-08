using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models
{
    [Table("MASTER_USER")]
    public class MasterUser
    {
        [Key]
        [Column("USER_ID")]
        public int UserId { get; set; }

        [Column("USERNAME")]
        public string Username { get; set; }

        [Column("EMAIL")]
        public string Email { get; set; }

        [Column("PASSWORD")]
        public string Password { get; set; }

        [Column("ROLE")]
        public string Role { get; set; }

        [Column("CREATED_DATE")]
        public DateTime CreatedDate { get; set; }

        [Column("IS_ACTIVE")]
        public byte IsActive { get; set; }

        [NotMapped]
        public bool IsActiveFlag
        {
            get => IsActive == 1;
            set => IsActive = value ? (byte)1 : (byte)0;
        }
    }
}
