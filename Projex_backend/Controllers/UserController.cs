using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Projex_backend.Data;
using Projex_backend.Dtos;
using Projex_backend.Helpers;

namespace Projex_backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _db;

        public UsersController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("search")]
        public IActionResult Search([FromQuery] UserSearchQuery filter)
        {
            var currentUserId = User.GetUserId();

            var users = _db.Users
                .Where(x => x.IsActive && x.Id != currentUserId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                users = users.Where(x =>
                    x.FullName.Contains(filter.Keyword) ||
                    x.Email.Contains(filter.Keyword));
            }

            var result = users
                .OrderBy(x => x.FullName)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.FullName
                })
                .ToPagedResult(filter.Page, filter.PageSize);

            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public IActionResult GetById(int id)
        {
            var user = _db.Users.Find(id);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            return Ok(new
            {
                user.Id,
                user.Email,
                user.FullName,
                user.PhoneNumber,
                user.AvatarUrl,
                user.IsActive,
                user.CreatedAt
            });
        }
    }
}
