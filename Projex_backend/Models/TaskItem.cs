using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net.Mail;

namespace Projex_backend.Models
{
    [Table("Tasks")]
    public class TaskItem
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }

        [MaxLength(255)]
        public string Title { get; set; } = null!;

        public string? Description { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = "Todo";

        public int Priority { get; set; } = 2;

        public DateTime? DueDate { get; set; }

        public int CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? StatusUpdatedAt { get; set; }

        public bool IsDeleted { get; set; }

        public Project Project { get; set; } = null!;

        public ICollection<TaskAssignment> Assignments { get; set; } = new List<TaskAssignment>();

        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    }
}