using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using MyPrivateWatchlist.Models;
using MyPrivateWatchlist.Services;

namespace MyPrivateWatchlist.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableCors("ExtensionPolicy")]
public class WatchlistController : ControllerBase
{
    private readonly ILogger<WatchlistController> _logger;
    private static List<WatchlistItem> _watchlist = new List<WatchlistItem>();

    public WatchlistController(ILogger<WatchlistController> logger)
    {
        _logger = logger;
    }

    [HttpPost("add")]
    public IActionResult AddMovie([FromBody] AddMovieRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.ImdbId))
            {
                return BadRequest(new { error = "IMDb ID is required" });
            }

            // Check if movie already exists
            var existingItem = _watchlist.FirstOrDefault(i => i.ImdbId == request.ImdbId);
            if (existingItem != null)
            {
                return Ok(new { 
                    success = false, 
                    message = "Movie already in watchlist"
                });
            }

            // Create new watchlist item
            var newItem = new WatchlistItem
            {
                ImdbId = request.ImdbId,
                Title = request.Title,
                TitleType = request.TitleType ?? "movie",
                Year = request.Year,
                Genres = request.Genres,
                Director = request.Director,
                ParsedYear = int.TryParse(request.Year, out var year) ? year : 0,
                Status = WatchlistStatus.Pending,
                DateAdded = DateTime.UtcNow
            };

            _watchlist.Add(newItem);

            return Ok(new { 
                success = true, 
                message = "Movie added to watchlist",
                item = newItem
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding movie via extension");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("remove")]
    public IActionResult RemoveMovie([FromBody] RemoveMovieRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.ImdbId))
            {
                return BadRequest(new { error = "IMDb ID is required" });
            }

            var item = _watchlist.FirstOrDefault(i => i.ImdbId == request.ImdbId);
            if (item == null)
            {
                return Ok(new { success = false, message = "Movie not found in watchlist" });
            }

            _watchlist.Remove(item);

            return Ok(new { success = true, message = "Movie removed from watchlist" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing movie via extension");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("check/{imdbId}")]
    public IActionResult CheckMovie(string imdbId)
    {
        try
        {
            var item = _watchlist.FirstOrDefault(i => i.ImdbId == imdbId);
            
            return Ok(new { 
                exists = item != null,
                isInWatchlist = item != null,
                item = item
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking movie via extension");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("list")]
    public IActionResult GetWatchlist()
    {
        try
        {
            return Ok(new { 
                items = _watchlist.OrderByDescending(i => i.DateAdded).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting watchlist");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

public class AddMovieRequest
{
    public string ImdbId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? TitleType { get; set; }
    public string? Year { get; set; }
    public string? Genres { get; set; }
    public string? Director { get; set; }
}

public class RemoveMovieRequest
{
    public string ImdbId { get; set; } = string.Empty;
}
