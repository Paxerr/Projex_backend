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
        private readonly IWebHostEnvironment _environment;

        public UsersController(AppDbContext db, IWebHostEnvironment environment)
        {
            _db = db;
            _environment = environment;
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

        [HttpPost("avatar/upload")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadAvatar([FromForm] UploadAvatarRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var id = User.GetUserId();
            var user = _db.Users.Find(id);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var file = request.File ?? Request.Form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "No file was uploaded." });
            }

            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Avatar must be an image file." });
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest(new { message = "Avatar file type is not supported." });
            }

            var webRootPath = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
            {
                webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");
            }

            var uploadFolder = Path.Combine(webRootPath, "uploads", "avatars");
            Directory.CreateDirectory(uploadFolder);

            var fileName = $"{user.Id}_{DateTime.Now:ddMMyyyyHHmmssfff}{extension}";
            var filePath = Path.Combine(uploadFolder, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var avatarUrl = $"{Request.Scheme}://{Request.Host}/uploads/avatars/{fileName}";

            user.AvatarUrl = avatarUrl;
            user.UpdatedAt = DateTime.Now;

            _db.SaveChanges();

            return Ok(new
            {
                message = "Upload avatar successfully.",
                avatarUrl
            });
        }
    }
}
