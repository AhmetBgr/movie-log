using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using MyPrivateWatchlist.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MyPrivateWatchlist.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableCors("ExtensionPolicy")]
public class MediaController : ControllerBase
{
    private static readonly string[] MediaExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };

    [HttpGet("scan")]
    public IActionResult Scan([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return BadRequest("Path is required");
        if (!Directory.Exists(path)) return NotFound("Directory not found");

        var results = new List<LocalMediaFile>();
        try
        {
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f => MediaExtensions.Contains(Path.GetExtension(f).ToLower()));

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                var fileName = Path.GetFileNameWithoutExtension(file);
                
                var (cleanName, year) = ParseFileName(fileName);

                results.Add(new LocalMediaFile
                {
                    FileName = Path.GetFileName(file),
                    FullPath = file,
                    CleanName = cleanName,
                    Year = year,
                    SizeBytes = info.Length
                });
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("play")]
    public IActionResult Play([FromBody] PlayMediaRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullPath)) return BadRequest("Path is required");
        if (!System.IO.File.Exists(request.FullPath)) return NotFound("File not found");

        try
        {
            Process.Start(new ProcessStartInfo(request.FullPath) { UseShellExecute = true });
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    private (string cleanName, int? year) ParseFileName(string fileName)
    {
        // Simple regex to find year like (2024) or 2024
        var yearMatch = Regex.Match(fileName, @"(?<=\b|\()(19|20)\d{2}(?=\b|\))");
        int? year = null;
        if (yearMatch.Success)
        {
            year = int.Parse(yearMatch.Value);
        }

        // Clean name: take everything before the year or some common keywords
        var cleanName = fileName;
        if (yearMatch.Success)
        {
            cleanName = fileName.Substring(0, yearMatch.Index).Trim();
        }

        // Remove things like ".", "_", and common tags
        cleanName = Regex.Replace(cleanName, @"[\._]", " ");
        cleanName = Regex.Replace(cleanName, @"\b(1080p|720p|4k|2160p|bluray|h264|x264|h265|x265|hevc|web-dl|webrip|brrip|dvdrip|multi|internal|repack)\b.*", "", RegexOptions.IgnoreCase);
        
        // Final cleanup of trailing characters like " - "
        cleanName = cleanName.Trim('-', ' ', '.');

        return (cleanName.Trim(), year);
    }
}
