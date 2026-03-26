namespace MyPrivateWatchlist.Models;

public sealed class OpenOnLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Template { get; set; } = "";
    public string SampleUrl { get; set; } = "";
    public string Host { get; set; } = "";
    public string SlugCaseMode { get; set; } = "title";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
