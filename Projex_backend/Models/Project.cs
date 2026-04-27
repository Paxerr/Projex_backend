using System.ComponentModel.DataAnnotations;

namespace Projex_backend.Models
{
    public class Project
    {
        public int Id { get; set; }

        [MaxLength(255)]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public int OwnerId { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = "Active";

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public User Owner { get; set; } = null!;

        public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();

        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    }
}