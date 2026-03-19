using Microsoft.AspNetCore.Mvc;

namespace MyPrivateWatchlist.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { message = "API is working!", timestamp = DateTime.UtcNow });
    }
}
