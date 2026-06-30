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
    [Route("api")]
    public class TasksController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TasksController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("projects/{projectId:int}/tasks")]
        public IActionResult GetByProject(int projectId, [FromQuery] TaskQuery filter)
        {
            var userId = User.GetUserId();

            if (!CanAccessProject(projectId, userId))
            {
                return Forbid();
            }

            var tasks = _db.Tasks
                .AsNoTracking()
                .Include(x => x.Assignments)
                    .ThenInclude(x => x.User)
                .Where(x => x.ProjectId == projectId && !x.IsDeleted)
                .AsQueryable();

            tasks = ApplyFilters(tasks, filter);
            tasks = ApplySorting(tasks, filter.SortBy, filter.SortOrder);

            var result = tasks
                .Select(x => new
                {
                    x.Id,
                    x.ProjectId,
                    x.Title,
                    x.Description,
                    x.Status,
                    x.Priority,
                    x.DueDate,
                    x.CreatedBy,
                    x.CreatedAt,
                    x.UpdatedAt,
                    x.StatusUpdatedAt,
                    assignees = x.Assignments.Select(a => new
                    {
                        a.UserId,
                        a.User.FullName,
                        a.User.Email
                    })
                })
                .ToPagedResult(filter.Page, filter.PageSize);

            return Ok(result);
        }

        [HttpGet("tasks/{id:int}")]
        public IActionResult GetById(int id)
        {
            var userId = User.GetUserId();

            var task = _db.Tasks
                .AsNoTracking()
                .Include(x => x.Assignments)
                    .ThenInclude(x => x.User)
                .FirstOrDefault(x => x.Id == id && !x.IsDeleted);

            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            if (!CanAccessProject(task.ProjectId, userId))
            {
                return Forbid();
            }

            return Ok(new
            {
                task.Id,
                task.ProjectId,
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DueDate,
                task.CreatedBy,
                task.CreatedAt,
                task.UpdatedAt,
                task.StatusUpdatedAt,
                assignees = task.Assignments.Select(a => new
                {
                    a.UserId,
                    a.User.FullName,
                    a.User.Email
                })
            });
        }

        [HttpGet("tasks/assigned/GetAllTask")]
        public IActionResult GetAllTasks()
        {
            var currentUserId = User.GetUserId();

            var accessibleProjectIds = _db.ProjectMembers
                .Where(pm => pm.UserId == currentUserId)
                .Select(pm => pm.ProjectId);

            var result = _db.Tasks
                .AsNoTracking()
                .Include(t => t.Project)
                .Include(t => t.Assignments)
                    .ThenInclude(a => a.User)
                .Where(t =>
                    !t.IsDeleted &&
                    accessibleProjectIds.Contains(t.ProjectId))
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.ProjectId,
                    project = new
                    {
                        t.Project.Id,
                        t.Project.Name,
                        t.Project.Code
                    },
                    t.Title,
                    t.Description,
                    t.Status,
                    t.Priority,
                    t.DueDate,
                    t.CreatedBy,
                    t.CreatedAt,
                    t.UpdatedAt,
                    t.StatusUpdatedAt,
                    assignees = t.Assignments.Select(a => new
                    {
                        a.UserId,
                        a.User.FullName,
                        a.User.Email
                    })
                })
                .ToList();

            return Ok(result);
        }


        [HttpGet("tasks/assigned")]
        public IActionResult GetAssignedTasks([FromQuery] TaskQuery filter)
        {
            var currentUserId = User.GetUserId();

            var accessibleProjectIds = _db.ProjectMembers
                .Where(x => x.UserId == currentUserId)
                .Select(x => x.ProjectId);

            var tasks = _db.Tasks
                .AsNoTracking()
                .Include(x => x.Assignments)
                    .ThenInclude(x => x.User)
                .Where(x =>
                    !x.IsDeleted &&
                    accessibleProjectIds.Contains(x.ProjectId) &&
                    x.Assignments.Any(a => a.UserId == currentUserId))
                .AsQueryable();

            tasks = ApplyFilters(tasks, filter);
            tasks = ApplySorting(tasks, filter.SortBy, filter.SortOrder);

            var result = tasks
                .Select(x => new
                {
                    x.Id,
                    x.ProjectId,
                    project = new
                    {
                        x.Project.Id,
                        x.Project.Name,
                        x.Project.Code
                    },
                    x.Title,
                    x.Description,
                    x.Status,
                    x.Priority,
                    x.DueDate,
                    x.CreatedBy,
                    x.CreatedAt,
                    x.UpdatedAt,
                    x.StatusUpdatedAt,
                    assignees = x.Assignments.Select(a => new
                    {
                        a.UserId,
                        a.User.FullName,
                        a.User.Email
                    })
                })
                .ToPagedResult(filter.Page, filter.PageSize);

            return Ok(result);
        }

        [HttpPost("projects/{projectId:int}/tasks")]
        public IActionResult Create(int projectId, [FromBody] CreateTaskRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.GetUserId();

            if (!CanManageProject(projectId, userId))
            {
                return Forbid();
            }

            if (!TaskStatusHelper.IsValid(request.Status))
            {
                return BadRequest(new { message = "Invalid task status." });
            }

            var assigneeIds = request.AssignedUserIds.Distinct().ToList();
            if (assigneeIds.Count > 0)
            {
                var memberIds = _db.ProjectMembers
                    .Where(x => x.ProjectId == projectId && assigneeIds.Contains(x.UserId))
                    .Select(x => x.UserId)
                    .ToHashSet();

                if (memberIds.Count != assigneeIds.Count)
                {
                    return BadRequest(new { message = "All assignees must belong to the project." });
                }
            }

            var task = new TaskItem
            {
                ProjectId = projectId,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                Status = TaskStatusHelper.Normalize(request.Status),
                Priority = request.Priority,
                DueDate = request.DueDate,
                CreatedBy = userId,
                CreatedAt = DateTime.Now,
                StatusUpdatedAt = DateTime.Now,
                IsDeleted = false
            };

            _db.Tasks.Add(task);
            _db.SaveChanges();

            if (assigneeIds.Count > 0)
            {
                _db.TaskAssignments.AddRange(assigneeIds.Select(x => new TaskAssignment
                {
                    TaskId = task.Id,
                    UserId = x,
                    AssignedAt = DateTime.Now
                }));
                _db.SaveChanges();

                _db.Notifications.AddRange(assigneeIds.Select(x => new Notification
                {
                    UserId = x,
                    ProjectId = projectId,
                    TaskId = task.Id,
                    TriggeredBy = userId,
                    Title = "New task assigned",
                    Message = $"You were assigned to task \"{task.Title}\".",
                    Type = "TaskAssigned",
                    CreatedAt = DateTime.Now
                }));
                _db.SaveChanges();
            }

            return Ok(new
            {
                task.Id,
                task.ProjectId,
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DueDate,
                task.CreatedBy,
                task.CreatedAt,
                task.UpdatedAt,
                task.StatusUpdatedAt,
                assignedUserIds = assigneeIds
            });
        }

        [HttpPut("tasks/{id:int}")]
        public IActionResult Update(int id, [FromBody] UpdateTaskRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.GetUserId();
            var task = _db.Tasks.FirstOrDefault(x => x.Id == id && !x.IsDeleted);

            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            if (!CanManageProject(task.ProjectId, userId))
            {
                return Forbid();
            }

            if (!TaskStatusHelper.IsValid(request.Status))
            {
                return BadRequest(new { message = "Invalid task status." });
            }

            var normalizedStatus = TaskStatusHelper.Normalize(request.Status);

            task.Title = request.Title.Trim();
            task.Description = request.Description?.Trim();
            task.Priority = request.Priority;
            task.DueDate = request.DueDate;
            task.UpdatedAt = DateTime.Now;

            if (!string.Equals(task.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
            {
                task.Status = normalizedStatus;
                task.StatusUpdatedAt = DateTime.Now;
            }

            _db.SaveChanges();

            return Ok(new
            {
                task.Id,
                task.ProjectId,
                task.Title,
                task.Description,
                task.Status,
                task.Priority,
                task.DueDate,
                task.CreatedBy,
                task.CreatedAt,
                task.UpdatedAt,
                task.StatusUpdatedAt
            });
        }


        [HttpPatch("tasks/{id:int}/status")]
        public IActionResult UpdateStatus(int id, [FromBody] UpdateTaskStatusRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.GetUserId();
            var task = _db.Tasks.FirstOrDefault(x => x.Id == id && !x.IsDeleted);

            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            if (!CanAccessProject(task.ProjectId, userId))
            {
                return Forbid();
            }

            if (!CanChangeStatusTask(task.Id, userId))
            {
                if(!CanManageProject(task.ProjectId, userId))
                {
                    return Forbid();
                }
            }

            if (!TaskStatusHelper.IsValid(request.Status))
            {
                return BadRequest(new { message = "Invalid task status." });
            }

            task.Status = TaskStatusHelper.Normalize(request.Status);
            task.StatusUpdatedAt = DateTime.Now;
            task.UpdatedAt = DateTime.Now;

            _db.SaveChanges();

            var assigneeIds = _db.TaskAssignments
                .Where(x => x.TaskId == task.Id)
                .Select(x => x.UserId)
                .Distinct()
                .ToList();

            if (assigneeIds.Count > 0)
            {
                _db.Notifications.AddRange(assigneeIds.Select(x => new Notification
                {
                    UserId = x,
                    ProjectId = task.ProjectId,
                    TaskId = task.Id,
                    TriggeredBy = userId,
                    Title = "Task status updated",
                    Message = $"Task \"{task.Title}\" changed to {task.Status}.",
                    Type = "TaskStatusChanged",
                    CreatedAt = DateTime.Now
                }));
                _db.SaveChanges();
            }

            return Ok(new
            {
                task.Id,
                task.ProjectId,
                task.Title,
                task.Status,
                task.StatusUpdatedAt,
                task.UpdatedAt
            });
        }

        [HttpDelete("tasks/{id:int}")]
        public IActionResult Delete(int id)
        {
            var userId = User.GetUserId();
            var task = _db.Tasks.FirstOrDefault(x => x.Id == id && !x.IsDeleted);

            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            if (!CanManageProject(task.ProjectId, userId))
            {
                return Forbid();
            }

            task.IsDeleted = true;
            task.UpdatedAt = DateTime.Now;
            _db.SaveChanges();

            return Ok(new { message = "Task deleted successfully." });
        }

        private IQueryable<TaskItem> ApplyFilters(IQueryable<TaskItem> query, TaskQuery filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                query = query.Where(x =>
                    x.Title.Contains(filter.Keyword) ||
                    (x.Description != null && x.Description.Contains(filter.Keyword)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                var status = TaskStatusHelper.Normalize(filter.Status);
                query = query.Where(x => x.Status == status);
            }

            if (filter.Priority.HasValue)
            {
                query = query.Where(x => x.Priority == filter.Priority.Value);
            }

            if (filter.AssignedUserId.HasValue)
            {
                query = query.Where(x => x.Assignments.Any(a => a.UserId == filter.AssignedUserId.Value));
            }

            if (filter.CreatedBy.HasValue)
            {
                query = query.Where(x => x.CreatedBy == filter.CreatedBy.Value);
            }

            if (filter.DueFrom.HasValue)
            {
                query = query.Where(x => x.DueDate >= filter.DueFrom.Value);
            }

            if (filter.DueTo.HasValue)
            {
                query = query.Where(x => x.DueDate <= filter.DueTo.Value);
            }

            if (filter.IsOverdue == true)
            {
                query = query.Where(x => x.DueDate != null && x.DueDate < DateTime.Now && x.Status != "Done");
            }

            return query;
        }

        private IQueryable<TaskItem> ApplySorting(IQueryable<TaskItem> query, string? sortBy, string? sortOrder)
        {
            var descending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);

            return sortBy?.Trim().ToLowerInvariant() switch
            {
                "title" => descending ? query.OrderByDescending(x => x.Title) : query.OrderBy(x => x.Title),
                "status" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
                "priority" => descending ? query.OrderByDescending(x => x.Priority) : query.OrderBy(x => x.Priority),
                "duedate" => descending ? query.OrderByDescending(x => x.DueDate) : query.OrderBy(x => x.DueDate),
                _ => query.OrderByDescending(x => x.Id)
            };
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
        private bool CanChangeStatusTask(int taskId, int userId)
        {
            var hasAssignee = _db.TaskAssignments
                .Any(x => x.TaskId == taskId);

            if (!hasAssignee)
            {
                return true;
            }

            return _db.TaskAssignments
                .Any(x => x.TaskId == taskId && x.UserId == userId);
        }
    }
}
