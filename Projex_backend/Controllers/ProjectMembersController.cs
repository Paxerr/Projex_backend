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

            var requestedMembers = request.Members
                .Where(x => x.UserId > 0)
                .GroupBy(x => x.UserId)
                .Select(x => x.First())
                .ToList();

            if (requestedMembers.Count == 0)
            {
                return BadRequest(new { message = "Members is required." });
            }

            var requestedUserIds = requestedMembers
                .Select(x => x.UserId)
                .ToList();

            var existingUserIds = _db.Users
                .Where(x => requestedUserIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToHashSet();

            var missingUserIds = requestedUserIds
                .Where(x => !existingUserIds.Contains(x))
                .ToList();

            if (missingUserIds.Count > 0)
            {
                return NotFound(new
                {
                    message = "Some users were not found.",
                    userIds = missingUserIds
                });
            }

            var alreadyMemberUserIds = _db.ProjectMembers
                .Where(x => x.ProjectId == projectId && requestedUserIds.Contains(x.UserId))
                .Select(x => x.UserId)
                .ToList();

            if (alreadyMemberUserIds.Count > 0)
            {
                return BadRequest(new
                {
                    message = "Some users are already members of this project.",
                    userIds = alreadyMemberUserIds
                });
            }

            var joinedAt = DateTime.Now;
            var members = requestedMembers
                .Select(x => new ProjectMember
                {
                    ProjectId = projectId,
                    UserId = x.UserId,
                    Role = NormalizeRole(x.Role),
                    JoinedAt = joinedAt
                })
                .ToList();

            _db.ProjectMembers.AddRange(members);
            _db.Notifications.AddRange(members.Select(x => new Notification
            {
                UserId = x.UserId,
                ProjectId = projectId,
                TriggeredBy = currentUserId,
                Title = "Added to project",
                Message = $"You were added to project #{projectId} as {x.Role}.",
                Type = "ProjectMemberAdded",
                CreatedAt = joinedAt
            }));
            _db.SaveChanges();

            return Ok(new
            {
                message = "Members added successfully.",
                addedCount = members.Count,
                members = members.Select(x => new
                {
                    x.UserId,
                    x.ProjectId,
                    x.Role,
                    x.JoinedAt
                })
            });
        }

        [HttpPost("by-email")]
        public IActionResult AddMemberByEmail(int projectId, [FromBody] AddProjectMemberByEmailRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var currentUserId = User.GetUserId();

            if (_db.Projects.Find(projectId) == null)
            {
                return NotFound(new { message = "Project not found." });
            }

            if (!CanManageProject(projectId, currentUserId))
            {
                return Forbid();
            }

            var requestedMembers = request.Members
                .Where(x => !string.IsNullOrWhiteSpace(x.Email))
                .GroupBy(x => x.Email.Trim().ToLowerInvariant())
                .Select(x => x.First())
                .ToList();

            if (requestedMembers.Count == 0)
            {
                return BadRequest(new { message = "Members is required." });
            }

            var requestedEmails = requestedMembers
                .Select(x => x.Email.Trim().ToLowerInvariant())
                .ToList();

            var users = _db.Users
                .Where(x => requestedEmails.Contains(x.Email.ToLower()))
                .Select(x => new
                {
                    x.Id,
                    x.Email
                })
                .ToList();

            var usersByEmail = users.ToDictionary(x => x.Email.ToLowerInvariant(), x => x.Id);
            var missingEmails = requestedEmails
                .Where(x => !usersByEmail.ContainsKey(x))
                .ToList();

            if (missingEmails.Count > 0)
            {
                return NotFound(new
                {
                    message = "Some users were not found.",
                    emails = missingEmails
                });
            }

            var requestedUserIds = usersByEmail.Values.ToList();
            var alreadyMemberUserIds = _db.ProjectMembers
                .Where(x => x.ProjectId == projectId && requestedUserIds.Contains(x.UserId))
                .Select(x => x.UserId)
                .ToList();

            if (alreadyMemberUserIds.Count > 0)
            {
                var alreadyMemberEmails = users
                    .Where(x => alreadyMemberUserIds.Contains(x.Id))
                    .Select(x => x.Email)
                    .ToList();

                return BadRequest(new
                {
                    message = "Some users are already members of this project.",
                    emails = alreadyMemberEmails
                });
            }

            var joinedAt = DateTime.Now;
            var members = requestedMembers
                .Select(x => new ProjectMember
                {
                    ProjectId = projectId,
                    UserId = usersByEmail[x.Email.Trim().ToLowerInvariant()],
                    Role = NormalizeRole(x.Role),
                    JoinedAt = joinedAt
                })
                .ToList();

            _db.ProjectMembers.AddRange(members);

            _db.Notifications.AddRange(members.Select(x => new Notification
            {
                UserId = x.UserId,
                ProjectId = projectId,
                TriggeredBy = currentUserId,
                Title = "Added to project",
                Message = $"You were added to project #{projectId} as {x.Role}.",
                Type = "ProjectMemberAdded",
                CreatedAt = joinedAt
            }));

            _db.SaveChanges();

            return Ok(new
            {
                message = "Members added successfully.",
                addedCount = members.Count,
                members = members.Select(x => new
                {
                    x.UserId,
                    x.ProjectId,
                    x.Role,
                    x.JoinedAt
                })
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
