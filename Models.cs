using System.Text.Json.Serialization;

namespace MyPrivateWatchlist.Models;

public class WatchlistItem
{
    public string ImdbId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string TitleType { get; set; } = null!;
    public string Year { get; set; } = null!;
    public string Genres { get; set; } = null!;
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

    // --- NEW EXTENDED FIELDS ---
    public List<string> Directors { get; set; } = new();
    public List<string> Actors { get; set; } = new();

    public string FullPosterUrl => string.IsNullOrEmpty(PosterPath)
        ? "https://via.placeholder.com/500x750?text=No+Poster"
        : $"https://image.tmdb.org/t/p/w500{PosterPath}";
}

public class TmdbTvResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

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