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
    public List<TmdbMovie>? movie_results { get; set; }
    public List<TmdbTvResult>? tv_results { get; set; }
}

public class TmdbMovie
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    [JsonPropertyName("original_title")] // Added this!
    public string? OriginalTitle { get; set; }

    public string Overview { get; set; } = "";

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    public string FullPosterUrl => string.IsNullOrEmpty(PosterPath)
        ? "https://via.placeholder.com/500x750?text=No+Poster"
        : $"https://image.tmdb.org/t/p/w500{PosterPath}";
}

public class TmdbTvResult
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string overview { get; set; } = "";

    [JsonPropertyName("poster_path")]
    public string? poster_path { get; set; }

    // --- NEW FIELDS ---
    [JsonPropertyName("first_air_date")]
    public string? first_air_date { get; set; }

    [JsonPropertyName("vote_average")]
    public double vote_average { get; set; }
    // ------------------
}