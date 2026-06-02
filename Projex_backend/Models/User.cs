using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace Projex_backend.Models
{
    public class User
    {
        public int Id { get; set; }

        [MaxLength(255)]
        public string Email { get; set; } = null!;

        [MaxLength(255)]
        public string PasswordHash { get; set; } = null!;

        [MaxLength(255)]
        public string FullName { get; set; } = null!;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(500)]
        public string? AvatarUrl { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public ICollection<Project> OwnedProjects { get; set; } = new List<Project>();
        public ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();
        public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
        public ICollection<TaskAssignment> TaskAssignments { get; set; } = new List<TaskAssignment>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
        public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    }
}
