using System.Text.Json.Serialization;

namespace MyPrivateWatchlist.Models;

public enum WatchlistStatus
{
    Pending,
    Watching,
    Watched
}

public class WatchlistItem
{
    public string ImdbId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string TitleType { get; set; } = null!;
    public string Year { get; set; } = null!;
    public string Genres { get; set; } = null!;
    public string? Director { get; set; }
    public string? OriginalTitle { get; set; }
    public int ParsedYear { get; set; }
    public WatchlistStatus Status { get; set; } = WatchlistStatus.Pending;
    public int? CurrentSeason { get; set; }
    public int? CurrentEpisode { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.Now;
    public int? UserRating { get; set; }

    public string? DisplayOriginalTitle 
    {
        get
        {
            if (string.IsNullOrEmpty(OriginalTitle) || string.Equals(OriginalTitle.Trim(), Title.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;
            return OriginalTitle.Trim();
        }
    }
}

public class TmdbFindResult
{
    [JsonPropertyName("movie_results")]
    public List<TmdbMovie>? MovieResults { get; set; }

    [JsonPropertyName("tv_results")]
    public List<TmdbTvResult>? TvResults { get; set; }
}

public class TmdbMovie
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    [JsonPropertyName("original_title")] 
    public string? OriginalTitle { get; set; }

    public string Overview { get; set; } = "";

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    // --- NEW EXTENDED FIELDS ---
    public List<string> Directors { get; set; } = new();
    public List<string> Actors { get; set; } = new();

    public string FullPosterUrl => string.IsNullOrEmpty(PosterPath)
        ? "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=" 
        : $"https://image.tmdb.org/t/p/w500{PosterPath}";
}

public class TmdbTvResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = "";

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }
}

// --- NEW TMDB CREDITS CLASSES ---
public class TmdbCredits
{
    [JsonPropertyName("cast")]
    public List<TmdbCast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<TmdbCrew> Crew { get; set; } = new();
}

public class TmdbCast
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class TmdbCrew
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("job")]
    public string Job { get; set; } = "";
}

public class TmdbSearchResponse
{
    [JsonPropertyName("results")]
    public List<TmdbSearchResultItem> Results { get; set; } = new();
}

public class TmdbSearchResultItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; } 

    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; } 

    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("genre_ids")]
    public List<int>? GenreIds { get; set; }

    public string DisplayTitle => Title ?? Name ?? "Unknown";

    public string? DisplayOriginalTitle 
    {
        get
        {
            var original = (OriginalTitle ?? OriginalName)?.Trim();
            var display = DisplayTitle?.Trim();
            if (string.IsNullOrEmpty(original) || string.Equals(original, display, StringComparison.OrdinalIgnoreCase))
                return null;
            return original;
        }
    }

    public string DisplayDate => ReleaseDate ?? FirstAirDate ?? "Unknown Date";
    public string FullPosterUrl => string.IsNullOrEmpty(PosterPath) 
        ? "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=" 
        : $"https://image.tmdb.org/t/p/w300{PosterPath}";
}

public class TmdbGenre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class TmdbGenreListResponse
{
    [JsonPropertyName("genres")]
    public List<TmdbGenre> Genres { get; set; } = new();
}