using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Projex_backend.Data;
using Projex_backend.Dtos;
using Projex_backend.Helpers;

namespace Projex_backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public NotificationsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult GetAll([FromQuery] NotificationQuery filter)
        {
            var userId = User.GetUserId();
            var query = _db.Notifications
                .Where(x => x.UserId == userId)
                .AsQueryable();

            if (filter.IsRead.HasValue)
            {
                query = query.Where(x => x.IsRead == filter.IsRead.Value);
            }

            var result = query
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.Message,
                    x.Type,
                    x.IsRead,
                    x.ProjectId,
                    x.TaskId,
                    x.CreatedAt
                })
                .ToPagedResult(filter.Page, filter.PageSize);

            return Ok(result);
        }

        [HttpPatch("{id:int}/read")]
        public IActionResult MarkAsRead(int id)
        {
            var userId = User.GetUserId();
            var notification = _db.Notifications.FirstOrDefault(x => x.Id == id && x.UserId == userId);

            if (notification == null)
            {
                return NotFound(new { message = "Notification not found." });
            }

            notification.IsRead = true;
            _db.SaveChanges();

            return Ok(new { message = "Notification marked as read." });
        }

        [HttpPatch("read-all")]
        public IActionResult MarkAllAsRead()
        {
            var userId = User.GetUserId();
            var notifications = _db.Notifications.Where(x => x.UserId == userId && !x.IsRead).ToList();

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            _db.SaveChanges();

            return Ok(new { message = "All notifications marked as read." });
        }
    }
}
