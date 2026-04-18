namespace MyPrivateWatchlist.Models;

public enum GistAutoSyncMode
{
    Disabled = 0,
    AutoPush = 1,
    AutoPull = 2,
    TwoWay = 3
}

public class GistSettings
{
    public string GistId { get; set; } = "";
    public string PersonalAccessToken { get; set; } = "";
    public GistAutoSyncMode AutoSyncMode { get; set; } = GistAutoSyncMode.Disabled;
    public int AutoPullIntervalMinutes { get; set; } = 5;
    public bool AutoSyncPaused { get; set; } = false;
    public string LocalLibraryPath { get; set; } = "";
}
