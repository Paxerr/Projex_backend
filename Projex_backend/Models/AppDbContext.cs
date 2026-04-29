using Microsoft.EntityFrameworkCore;
using Projex_backend.Models;

namespace Projex_backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectMember> ProjectMembers { get; set; }
        public DbSet<TaskItem> Tasks { get; set; }
        public DbSet<TaskAssignment> TaskAssignments { get; set; }
        public DbSet<Attachment> Attachments { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(x => x.Email)
                .IsUnique();

            modelBuilder.Entity<Project>()
                .HasIndex(x => x.Code)
                .IsUnique()
                .HasFilter("[Code] IS NOT NULL");

            modelBuilder.Entity<ProjectMember>()
                .HasKey(x => new { x.UserId, x.ProjectId });

            modelBuilder.Entity<TaskAssignment>()
                .HasKey(x => new { x.TaskId, x.UserId });

            modelBuilder.Entity<Notification>()
                .HasOne(x => x.TriggeredByUser)
                .WithMany()
                .HasForeignKey(x => x.TriggeredBy)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
