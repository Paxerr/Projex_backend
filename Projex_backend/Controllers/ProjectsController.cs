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
    [Route("api/projects")]
    public class ProjectsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ProjectsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult GetAll([FromQuery] ProjectQuery filter)
        {
            var userId = User.GetUserId();

            var projects = _db.Projects
                .AsNoTracking()
                .Include(x => x.Members)
                .Where(x => x.Members.Any(m => m.UserId == userId))
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                projects = projects.Where(x =>
                    x.Name.Contains(filter.Keyword) ||
                    x.Code!.Contains(filter.Keyword) ||
                    (x.Description != null && x.Description.Contains(filter.Keyword)));
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                projects = projects.Where(x => x.Status == filter.Status);
            }

            projects = ApplySorting(projects, filter.SortBy, filter.SortOrder);

            var result = projects
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Code,
                    x.Description,
                    x.Status,
                    x.StartDate,
                    x.EndDate,
                    x.OwnerId,
                    memberCount = x.Members.Count
                })
                .ToPagedResult(filter.Page, filter.PageSize);

            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public IActionResult GetById(int id)
        {
            var userId = User.GetUserId();

            if (!CanAccessProject(id, userId))
            {
                return Forbid();
            }

            var project = _db.Projects
                .AsNoTracking()
                .Include(x => x.Members)
                    .ThenInclude(x => x.User)
                .FirstOrDefault(x => x.Id == id);

            if (project == null)
            {
                return NotFound(new { message = "Project not found." });
            }

            return Ok(new
            {
                project.Id,
                project.Name,
                project.Code,
                project.Description,
                project.Status,
                project.StartDate,
                project.EndDate,
                project.OwnerId,
                members = project.Members.Select(m => new
                {
                    m.UserId,
                    m.Role,
                    m.JoinedAt,
                    user = new
                    {
                        m.User.Id,
                        m.User.Email,
                        m.User.FullName,
                        m.User.AvatarUrl
                    }
                })
            });
        }

        [HttpPost]
        public IActionResult Create([FromBody] CreateProjectRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (_db.Projects.Any(x => x.Code == request.Code))
            {
                return BadRequest(new { message = "Project code already exists." });
            }

            var userId = User.GetUserId();
            var project = new Project
            {
                Name = request.Name.Trim(),
                Code = request.Code.Trim(),
                Description = request.Description?.Trim(),
                OwnerId = userId,
                Status = "Active",
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                CreatedAt = DateTime.Now
            };

            _db.Projects.Add(project);
            _db.SaveChanges();

            _db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = project.Id,
                UserId = userId,
                Role = "Owner",
                JoinedAt = DateTime.Now
            });
            _db.SaveChanges();

            return Ok(new
            {
                project.Id,
                project.Name,
                project.Code,
                project.Description,
                project.Status,
                project.StartDate,
                project.EndDate,
                project.OwnerId,
                project.CreatedAt
            });
        }

        [HttpPut("{id:int}")]
        public IActionResult Update(int id, [FromBody] UpdateProjectRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.GetUserId();
            var project = _db.Projects.Find(id);

            if (project == null)
            {
                return NotFound(new { message = "Project not found." });
            }

            if (!CanManageProject(id, userId))
            {
                return Forbid();
            }

            if (_db.Projects.Any(x => x.Code == request.Code && x.Id != id))
            {
                return BadRequest(new { message = "Project code already exists." });
            }

            project.Name = request.Name.Trim();
            project.Code = request.Code.Trim();
            project.Description = request.Description?.Trim();
            project.Status = request.Status.Trim();
            project.StartDate = request.StartDate;
            project.EndDate = request.EndDate;
            project.UpdatedAt = DateTime.Now;

            _db.SaveChanges();

            return Ok(project);
        }

        [HttpPatch("{id:int}/status")]
        public IActionResult ChangeStatus(int id, [FromBody] UpdateProjectStatusRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.GetUserId();
            var project = _db.Projects.Find(id);

            if (project == null)
            {
                return NotFound(new { message = "Project not found." });
            }

            if (!CanManageProject(id, userId))
            {
                return Forbid();
            }

            project.Status = request.Status.Trim();
            project.UpdatedAt = DateTime.Now;

            _db.SaveChanges();

            return Ok(project);
        }

        [HttpDelete("{id:int}")]
        public IActionResult Delete(int id)
        {
            var userId = User.GetUserId();
            var project = _db.Projects.Find(id);

            if (project == null)
            {
                return NotFound(new { message = "Project not found." });
            }

            if (project.OwnerId != userId)
            {
                return Forbid();
            }

            _db.Projects.Remove(project);
            _db.SaveChanges();

            return Ok(new { message = "Project deleted successfully." });
        }

        [HttpGet("{id:int}/summary")]
        public IActionResult Summary(int id)
        {
            var userId = User.GetUserId();

            if (!CanAccessProject(id, userId))
            {
                return Forbid();
            }

            var totalTasks = _db.Tasks.Count(x => x.ProjectId == id && !x.IsDeleted);
            var assignedTasks = _db.Tasks.Count(x => x.ProjectId == id && !x.IsDeleted && x.Status == "Assigned");
            var inProgressTasks = _db.Tasks.Count(x => x.ProjectId == id && !x.IsDeleted && x.Status == "InProgress");
            var doneTasks = _db.Tasks.Count(x => x.ProjectId == id && !x.IsDeleted && x.Status == "Done");
            var members = _db.ProjectMembers.Count(x => x.ProjectId == id);

            return Ok(new
            {
                projectId = id,
                totalTasks,
                assignedTasks,
                inProgressTasks,
                doneTasks,
                members
            });
        }

        private IQueryable<Project> ApplySorting(IQueryable<Project> query, string? sortBy, string? sortOrder)
        {
            var descending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);

            return sortBy?.Trim().ToLowerInvariant() switch
            {
                "name" => descending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
                "code" => descending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
                "status" => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
                "startdate" => descending ? query.OrderByDescending(x => x.StartDate) : query.OrderBy(x => x.StartDate),
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
    }
}
