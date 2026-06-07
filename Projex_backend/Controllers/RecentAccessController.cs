using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Projex_backend.Data;
using Projex_backend.Helpers;
using Projex_backend.Models;

namespace Projex_backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/access")]
    public class RecentAccessController : ControllerBase
    {
        private readonly AppDbContext _db;

        public RecentAccessController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost("task/{taskId:int}")]
        public IActionResult PostAccess(int taskId)
        {
            var userId = User.GetUserId();

            RecentAccesses RA = new RecentAccesses();
            RA.UserId = userId;
            RA.TaskId = taskId;
            RA.AccessAt = DateTime.Now;

            _db.RecentAccesses.Add(RA);
            _db.SaveChanges();

            return Ok(new { message = "Access recorded successfully" });
        }

        [HttpGet]
        public IActionResult GetRecentAccesses()
        {
            var userId = User.GetUserId();

            var recentAccesses = _db.RecentAccesses
                .Where(ra => ra.UserId == userId)
                .OrderByDescending(ra => ra.AccessAt)
                .Take(4)
                .Select(ra => new
                {
                    ra.TaskId,
                    ra.AccessAt
                })
                .ToList();

            return Ok(recentAccesses);
        }
        //public bool CanAccessTask(int taskId, int userId)
        //{
        //    var hasAssignee = _db.TaskAssignments
        //        .Any(x => x.TaskId == taskId);
        //    return true;
        //}
    }
}
