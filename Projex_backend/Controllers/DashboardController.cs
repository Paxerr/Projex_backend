using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projex_backend.Data;
using Projex_backend.Dtos;
using Projex_backend.Helpers;

namespace Projex_backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly AppDbContext _db;

        public DashboardController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("overview")]
        public IActionResult GetOverview()
        {
            var userId = User.GetUserId();
            var myTaskIds = _db.TaskAssignments
                .Where(x => x.UserId == userId)
                .Select(x => x.TaskId);

            return Ok(new
            {
                myProjects = _db.ProjectMembers.Count(x => x.UserId == userId),
                myTasks = _db.Tasks.Count(x => myTaskIds.Contains(x.Id) && !x.IsDeleted),
                inProgressTasks = _db.Tasks.Count(x => myTaskIds.Contains(x.Id) && !x.IsDeleted && x.Status == "InProgress"),
                completedTasks = _db.Tasks.Count(x => myTaskIds.Contains(x.Id) && !x.IsDeleted && x.Status == "Done"),
                overdueTasks = _db.Tasks.Count(x => myTaskIds.Contains(x.Id) && !x.IsDeleted && x.DueDate != null && x.DueDate < DateTime.UtcNow && x.Status != "Done"),
                unreadNotifications = _db.Notifications.Count(x => x.UserId == userId && !x.IsRead),
                onTimeRate = CalculateOnTimeRate(userId)
            });
        }

        [HttpGet("my-tasks")]
        public IActionResult GetMyTasks([FromQuery] TaskQuery filter)
        {
            var userId = User.GetUserId();

            var tasks = _db.TaskAssignments
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => x.Task)
                .Where(x => !x.IsDeleted)
                .Include(x => x.Project)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                var status = TaskStatusHelper.Normalize(filter.Status);
                tasks = tasks.Where(x => x.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                tasks = tasks.Where(x =>
                    x.Title.Contains(filter.Keyword) ||
                    (x.Description != null && x.Description.Contains(filter.Keyword)));
            }

            var result = tasks
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.Status,
                    x.Priority,
                    x.DueDate,
                    project = new
                    {
                        x.ProjectId,
                        x.Project.Name
                    }
                })
                .ToPagedResult(filter.Page, filter.PageSize);

            return Ok(result);
        }

        private double CalculateOnTimeRate(int userId)
        {
            var assignedTasks = _db.TaskAssignments
                .Where(x => x.UserId == userId)
                .Select(x => x.Task)
                .Where(x => !x.IsDeleted && x.Status == "Done")
                .ToList();

            if (assignedTasks.Count == 0)
            {
                return 100;
            }

            var onTimeCount = assignedTasks.Count(x => x.DueDate == null || x.UpdatedAt == null || x.UpdatedAt <= x.DueDate);
            return Math.Round(onTimeCount * 100d / assignedTasks.Count, 2);
        }
    }
}
