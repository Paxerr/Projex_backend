using System.ComponentModel.DataAnnotations;

namespace Projex_backend.Models
{
    public class Attachment
    {
        public int Id { get; set; }

        public int TaskId { get; set; }

        [MaxLength(500)]
        public string FileUrl { get; set; } = null!;

        public int UploadedBy { get; set; }

        public DateTime UploadedAt { get; set; }

        public TaskItem Task { get; set; } = null!;

        public User Uploader { get; set; } = null!;
    }
}