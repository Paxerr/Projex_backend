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
    [Route("api/projects/{projectId:int}/members")]
    public class ProjectMembersController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ProjectMembersController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult GetMembers(int projectId, [FromQuery] MemberQuery filter)
        {
            var userId = User.GetUserId();

            if (!CanAccessProject(projectId, userId))
            {
                return Forbid();
            }

            var members = _db.ProjectMembers
                .AsNoTracking()
                .Include(x => x.User)
                .Where(x => x.ProjectId == projectId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                members = members.Where(x =>
                    x.User.FullName.Contains(filter.Keyword) ||
                    x.User.Email.Contains(filter.Keyword));
            }

            if (!string.IsNullOrWhiteSpace(filter.Role))
            {
                members = members.Where(x => x.Role == filter.Role);
            }

            var result = members
                .OrderBy(x => x.User.FullName)
                .Select(x => new
                {
                    x.UserId,
                    x.ProjectId,
                    x.Role,
                    x.JoinedAt,
                    user = new
                    {
                        x.User.Id,
                        x.User.Email,
                        x.User.FullName,
                        x.User.IsActive
                    }
                })
                .ToPagedResult(filter.Page, filter.PageSize);

            return Ok(result);
        }

        [HttpPost]
        public IActionResult AddMember(int projectId, [FromBody] AddProjectMemberRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var currentUserId = User.GetUserId();

            if (!CanManageProject(projectId, currentUserId))
            {
                return Forbid();
            }

            if (_db.Projects.Find(projectId) == null)
            {
                return NotFound(new { message = "Project not found." });
            }

            if (_db.Users.Find(request.UserId) == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (_db.ProjectMembers.Any(x => x.ProjectId == projectId && x.UserId == request.UserId))
            {
                return BadRequest(new { message = "User is already a member of this project." });
            }

            var member = new ProjectMember
            {
                ProjectId = projectId,
                UserId = request.UserId,
                Role = NormalizeRole(request.Role),
                JoinedAt = DateTime.UtcNow
            };

            _db.ProjectMembers.Add(member);
            _db.SaveChanges();

            _db.Notifications.Add(new Notification
            {
                UserId = request.UserId,
                ProjectId = projectId,
                TriggeredBy = currentUserId,
                Title = "Added to project",
                Message = $"You were added to project #{projectId} as {member.Role}.",
                Type = "ProjectMemberAdded",
                CreatedAt = DateTime.UtcNow
            });
            _db.SaveChanges();

            return Ok(new
            {
                member.UserId,
                member.ProjectId,
                member.Role,
                member.JoinedAt
            });
        }

        [HttpPost("by-email")]
        public IActionResult AddMemberByEmail(int projectId, [FromBody] AddProjectMemberByEmailRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var currentUserId = User.GetUserId();

            if (_db.Projects.Find(projectId) == null)
                return NotFound(new { message = "Project not found." });

            if (!CanManageProject(projectId, currentUserId))
                return Forbid();

            var userId = _db.Users
                .Where(x => x.Email == request.Email)
                .Select(x => x.Id)
                .FirstOrDefault();

            if (userId == 0)
                return NotFound(new { message = "User not found." });

            if (_db.ProjectMembers.Any(x => x.ProjectId == projectId && x.UserId == userId))
                return BadRequest(new { message = "User is already a member of this project." });

            var member = new ProjectMember
            {
                ProjectId = projectId,
                UserId = userId,
                Role = NormalizeRole(request.Role),
                JoinedAt = DateTime.UtcNow
            };

            _db.ProjectMembers.Add(member);

            _db.Notifications.Add(new Notification
            {
                UserId = userId,
                ProjectId = projectId,
                TriggeredBy = currentUserId,
                Title = "Added to project",
                Message = $"You were added to project #{projectId} as {member.Role}.",
                Type = "ProjectMemberAdded",
                CreatedAt = DateTime.UtcNow
            });

            _db.SaveChanges();

            return Ok(new
            {
                member.UserId,
                member.ProjectId,
                member.Role,
                member.JoinedAt
            });
        }

        [HttpPut("{userId:int}/role")]
        public IActionResult UpdateRole(int projectId, int userId, [FromBody] UpdateProjectMemberRoleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var currentUserId = User.GetUserId();

            if (!CanManageProject(projectId, currentUserId))
            {
                return Forbid();
            }

            var member = _db.ProjectMembers.FirstOrDefault(x => x.ProjectId == projectId && x.UserId == userId);

            if (member == null)
            {
                return NotFound(new { message = "Project member not found." });
            }

            if (member.Role == "Owner")
            {
                return BadRequest(new { message = "Owner role cannot be changed." });
            }

            member.Role = NormalizeRole(request.Role);
            _db.SaveChanges();

            return Ok(member);
        }

        [HttpDelete("{userId:int}")]
        public IActionResult RemoveMember(int projectId, int userId)
        {
            var currentUserId = User.GetUserId();

            if (!CanManageProject(projectId, currentUserId))
            {
                return Forbid();
            }

            var member = _db.ProjectMembers.FirstOrDefault(x => x.ProjectId == projectId && x.UserId == userId);

            if (member == null)
            {
                return NotFound(new { message = "Project member not found." });
            }

            if (member.Role == "Owner")
            {
                return BadRequest(new { message = "Owner cannot be removed." });
            }

            _db.ProjectMembers.Remove(member);
            _db.SaveChanges();

            return Ok(new { message = "Member removed successfully." });
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

        private static string NormalizeRole(string role)
        {
            return role.Trim().ToLowerInvariant() switch
            {
                "owner" => "Owner",
                "admin" => "Admin",
                _ => "Member"
            };
        }
    }
}
