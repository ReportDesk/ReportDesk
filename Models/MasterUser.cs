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
        public string Username { get; set; } = string.Empty;

        [Column("EMAIL")]
        public string Email { get; set; } = string.Empty;

        [Column("PASSWORD")]
        public string Password { get; set; } = string.Empty;

        [Column("ROLE")]
        public string Role { get; set; } = string.Empty;

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
