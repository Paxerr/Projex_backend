using System.ComponentModel.DataAnnotations;

namespace Projex_backend.Models
{
    public class ProjectMember
    {
        public int UserId { get; set; }

        public int ProjectId { get; set; }

        [MaxLength(50)]
        public string Role { get; set; } = null!;

        public DateTime JoinedAt { get; set; }

        public User User { get; set; } = null!;

        public Project Project { get; set; } = null!;
    }
}