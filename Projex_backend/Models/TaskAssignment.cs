namespace Projex_backend.Models
{
    public class TaskAssignment
    {
        public int TaskId { get; set; }

        public int UserId { get; set; }

        public DateTime AssignedAt { get; set; }

        public TaskItem Task { get; set; } = null!;

        public User User { get; set; } = null!;
    }
}