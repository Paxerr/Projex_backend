using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projex_backend.Data;
using Projex_backend.Dtos;
using Projex_backend.Helpers;
using Projex_backend.Models;

namespace Projex_backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/tasks/{taskId:int}/assignments")]
    public class TaskAssignmentsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TaskAssignmentsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult GetAssignments(int taskId)
        {
            var userId = User.GetUserId();
            var task = _db.Tasks.FirstOrDefault(x => x.Id == taskId && !x.IsDeleted);

            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            if (!CanAccessProject(task.ProjectId, userId))
            {
                return Forbid();
            }

            var items = _db.TaskAssignments
                .AsNoTracking()
                .Where(x => x.TaskId == taskId)
                .Include(x => x.User)
                .Select(x => new
                {
                    x.UserId,
                    x.AssignedAt,
                    user = new
                    {
                        x.User.Id,
                        x.User.Email,
                        x.User.FullName
                    }
                })
                .ToList();

            return Ok(new { items });
        }

        [HttpPost]
        public IActionResult AssignUsers(int taskId, [FromBody] AssignUsersRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var currentUserId = User.GetUserId();
            var task = _db.Tasks.FirstOrDefault(x => x.Id == taskId && !x.IsDeleted);

            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            if (!CanManageProject(task.ProjectId, currentUserId))
            {
                return Forbid();
            }

            var userIds = request.UserIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (userIds.Count == 0)
            {
                return BadRequest(new { message = "UserIds is required." });
            }

            var validMemberIds = _db.ProjectMembers
                .Where(x => x.ProjectId == task.ProjectId && userIds.Contains(x.UserId))
                .Select(x => x.UserId)
                .ToHashSet();

            if (validMemberIds.Count != userIds.Count)
            {
                return BadRequest(new { message = "All assignees must belong to the project." });
            }

            var existingUserIds = _db.TaskAssignments
                .Where(x => x.TaskId == taskId && userIds.Contains(x.UserId))
                .Select(x => x.UserId)
                .ToHashSet();

            var newAssignments = userIds
                .Where(x => !existingUserIds.Contains(x))
                .Select(x => new TaskAssignment
                {
                    TaskId = taskId,
                    UserId = x,
                    AssignedAt = DateTime.Now
                })
                .ToList();

            if (newAssignments.Count > 0)
            {
                _db.TaskAssignments.AddRange(newAssignments);
                _db.SaveChanges();

                _db.Notifications.AddRange(newAssignments.Select(x => new Notification
                {
                    UserId = x.UserId,
                    ProjectId = task.ProjectId,
                    TaskId = taskId,
                    TriggeredBy = currentUserId,
                    Title = "New task assigned",
                    Message = $"You were assigned to task \"{task.Title}\".",
                    Type = "TaskAssigned",
                    CreatedAt = DateTime.Now
                }));
                _db.SaveChanges();
            }

            return Ok(new
            {
                message = "Users assigned successfully.",
                addedCount = newAssignments.Count
            });
        }


        [HttpDelete("{userId:int}")]
        public IActionResult RemoveAssignment(int taskId, int userId)
        {
            var currentUserId = User.GetUserId();
            var task = _db.Tasks.FirstOrDefault(x => x.Id == taskId && !x.IsDeleted);

            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            if (!CanManageProject(task.ProjectId, currentUserId))
            {
                return Forbid();
            }

            var assignment = _db.TaskAssignments.FirstOrDefault(x => x.TaskId == taskId && x.UserId == userId);

            if (assignment == null)
            {
                return NotFound(new { message = "Assignment not found." });
            }

            _db.TaskAssignments.Remove(assignment);
            _db.SaveChanges();

            return Ok(new { message = "Assignment removed successfully." });
        }

        private bool CanAccessProject(int projectId, int userId)
        {
            return _db.ProjectMembers.Any(x => x.ProjectId == projectId && x.UserId == userId);
        }

        private bool CanManageProject(int projectId, int userId)
        {
            return _db.ProjectMembers.Any(x =>
                x.ProjectId == projectId &&
                x.UserId == userId &&
                (x.Role == "Owner" || x.Role == "Admin"));
        }
    }
}
