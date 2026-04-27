using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Projex_backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        [HttpGet]
            public IActionResult Get()
            {
                return Ok("Hello from UserController!");
        }
    }
}
