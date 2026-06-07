using Projex_backend.Models;

public class RecentAccesses
{
    public int UserId { get; set; }
    public int TaskId { get; set; }
    public DateTime AccessAt { get; set; }

    public User User { get; set; }
    public TaskItem Task { get; set; }
}