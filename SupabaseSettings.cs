namespace MyPrivateWatchlist.Models;

public class SupabaseSettings
{
    public string ProjectUrl { get; set; } = "";
    public string AnonKey { get; set; } = "";
    public string Email { get; set; } = "";
}

public class SupabaseSession
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
}
