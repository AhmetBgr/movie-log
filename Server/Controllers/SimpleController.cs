using Microsoft.AspNetCore.Mvc;

namespace MyPrivateWatchlist.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SimpleController : ControllerBase
{
    [HttpPost("add")]
    public IActionResult AddMovie([FromBody] dynamic request)
    {
        return Ok(new { success = true, message = "Movie added to watchlist" });
    }

    [HttpPost("remove")]
    public IActionResult RemoveMovie([FromBody] dynamic request)
    {
        return Ok(new { success = true, message = "Movie removed from watchlist" });
    }

    [HttpGet("check/{imdbId}")]
    public IActionResult CheckMovie(string imdbId)
    {
        return Ok(new { exists = false, isInWatchlist = false });
    }
}
