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
    [Route("api/tasks/{taskId:int}/attachments")]
    public class AttachmentsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public AttachmentsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult GetAll(int taskId)
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

            var items = _db.Attachments
                .AsNoTracking()
                .Where(x => x.TaskId == taskId)
                .Include(x => x.Uploader)
                .Select(x => new
                {
                    x.Id,
                    x.FileUrl,
                    x.UploadedBy,
                    x.UploadedAt,
                    uploader = new
                    {
                        x.Uploader.Id,
                        x.Uploader.Email,
                        x.Uploader.FullName
                    }
                })
                .ToList();

            return Ok(new { items });
        }

        [HttpPost]
        public IActionResult Create(int taskId, [FromBody] CreateAttachmentRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

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

            var attachment = new Attachment
            {
                TaskId = taskId,
                FileUrl = request.FileUrl.Trim(),
                UploadedBy = userId,
                UploadedAt = DateTime.UtcNow
            };

            _db.Attachments.Add(attachment);
            _db.SaveChanges();

            return Ok(new
            {
                attachment.Id,
                attachment.TaskId,
                attachment.FileUrl,
                attachment.UploadedBy,
                attachment.UploadedAt
            });
        }

        [HttpDelete("{attachmentId:int}")]
        public IActionResult Delete(int taskId, int attachmentId)
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

            var attachment = _db.Attachments.FirstOrDefault(x => x.Id == attachmentId && x.TaskId == taskId);

            if (attachment == null)
            {
                return NotFound(new { message = "Attachment not found." });
            }

            _db.Attachments.Remove(attachment);
            _db.SaveChanges();

            return Ok(new { message = "Attachment deleted successfully." });
        }

        private bool CanAccessProject(int projectId, int userId)
        {
            return _db.ProjectMembers.Any(x => x.ProjectId == projectId && x.UserId == userId);
        }
    }
}
