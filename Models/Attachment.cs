using Microsoft.Identity.Client;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportSystem.Models
{
    [Table("ATTACHMENT")]
    public class Attachment
    {
        [Key]
        [Column("ATTACHMENT_ID")]
        public int AttachmentId { get; set; }

        [Column("REPORT_ID")]
        public int ReportId { get; set; }

        [Column("FOLLOW_UP_ID")]
        public int FollowUpId { get; set; }

        [Column("FILE_NAME")]
        public string FileName { get; set; }

        [Column("FILE_PATH")]
        public string FilePath { get; set; }

        [Column("FILE_SIZE")]
        public int FileSize { get; set; }

        [Column("FILE_TYPE")]
        public string FileType { get; set; }

        [Column("UPLOAD_DATE")]
        public DateTime UploadDate { get; set; }

        [Column("UPLOADED_BY")]
        public string UploadedBy { get; set; }


    }
}
