using System.ComponentModel.DataAnnotations;

namespace Projex_backend.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int? ProjectId { get; set; }
        public int? TaskId { get; set; }
        public int? TriggeredBy { get; set; }

        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Type { get; set; } = string.Empty;

        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }

        public User User { get; set; } = null!;
        public Project? Project { get; set; }
        public TaskItem? Task { get; set; }
        public User? TriggeredByUser { get; set; }
    }
}
