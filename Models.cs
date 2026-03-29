using System.Text.Json.Serialization;

namespace MyPrivateWatchlist.Models;

public enum DataFetchPreference
{
    OnDemand,
    Background
}

public enum RatingSystem
{
    HundredPoint
}

public enum WatchlistStatus
{
    Pending,
    Watching,
    Watched
}

public enum GenreLogic
{
    Any, // OR
    All  // AND
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
    public int? Rating20 { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public int? TmdbId { get; set; }
    public int? Runtime { get; set; }
    public double? VoteAverage { get; set; }
    public TmdbCollection? Collection { get; set; }

    public string? DisplayOriginalTitle 
    {
        get
        {
            if (string.IsNullOrEmpty(OriginalTitle) || string.Equals(OriginalTitle.Trim(), Title.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;
            return OriginalTitle.Trim();
        }
    }

    public string FullPosterUrl => string.IsNullOrEmpty(PosterPath)
        ? "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=" 
        : $"https://image.tmdb.org/t/p/w185{PosterPath}";
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

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    // --- NEW EXTENDED FIELDS ---
    public TmdbCredits? Credits { get; set; }
    public List<string> BackdropPaths { get; set; } = new();
    public string? TrailerKey { get; set; }

    [JsonPropertyName("belongs_to_collection")]
    public TmdbCollection? Collection { get; set; }

    [JsonPropertyName("genres")]
    public List<TmdbGenre> GenreList { get; set; } = new();

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

    [JsonPropertyName("genres")]
    public List<TmdbGenre> GenreList { get; set; } = new();

    [JsonPropertyName("episode_run_time")]
    public List<int> EpisodeRunTime { get; set; } = new();
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
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    public string FullProfileUrl => string.IsNullOrEmpty(ProfilePath)
        ? "https://www.themoviedb.org/assets/2/v4/glyphicons/basic/glyphicons-basic-4-user-grey-d8fe574e3425d038c5d392ed9342517a9494391.svg"
        : $"https://image.tmdb.org/t/p/w185{ProfilePath}";
}

public class TmdbCrew
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("job")]
    public string Job { get; set; } = "";

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    public string FullProfileUrl => string.IsNullOrEmpty(ProfilePath)
        ? "https://www.themoviedb.org/assets/2/v4/glyphicons/basic/glyphicons-basic-4-user-grey-d8fe574e3425d038c5d392ed9342517a9494391.svg"
        : $"https://image.tmdb.org/t/p/w185{ProfilePath}";
}

public class TmdbPerson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("biography")]
    public string? Biography { get; set; }

    [JsonPropertyName("birthday")]
    public string? Birthday { get; set; }

    [JsonPropertyName("deathday")]
    public string? Deathday { get; set; }

    [JsonPropertyName("place_of_birth")]
    public string? PlaceOfBirth { get; set; }

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    [JsonPropertyName("known_for_department")]
    public string? KnownForDepartment { get; set; }

    [JsonPropertyName("gender")]
    public int Gender { get; set; }

    [JsonPropertyName("also_known_as")]
    public List<string>? AlsoKnownAs { get; set; }

    public string FullProfileUrl => string.IsNullOrEmpty(ProfilePath)
        ? "https://www.themoviedb.org/assets/2/v4/glyphicons/basic/glyphicons-basic-4-user-grey-d8fe574e3425d038c5d392ed9342517a9494391.svg"
        : $"https://image.tmdb.org/t/p/w500{ProfilePath}";
}

public class TmdbPersonCombinedCredits
{
    [JsonPropertyName("cast")]
    public List<TmdbPersonCreditItem> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<TmdbPersonCreditItem> Crew { get; set; } = new();
}

public class TmdbPersonCreditItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("character")]
    public string? Character { get; set; }

    [JsonPropertyName("job")]
    public string? Job { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    public string FullBackdropUrl => string.IsNullOrEmpty(BackdropPath)
        ? ""
        : $"https://image.tmdb.org/t/p/w1280{BackdropPath}";

    public string DisplayTitle => Title ?? Name ?? "Unknown";
    public string DisplayDate => ReleaseDate ?? FirstAirDate ?? "";
    public int Year => int.TryParse(DisplayDate.Split('-')[0], out var y) ? y : 0;

    public string FullPosterUrl => string.IsNullOrEmpty(PosterPath)
        ? "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="
        : $"https://image.tmdb.org/t/p/w185{PosterPath}";
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

    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    [JsonPropertyName("known_for_department")]
    public string? KnownForDepartment { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("genre_ids")]
    public List<int>? GenreIds { get; set; }

    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

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

    public string FullProfileUrl => string.IsNullOrEmpty(ProfilePath)
        ? "https://www.themoviedb.org/assets/2/v4/glyphicons/basic/glyphicons-basic-4-user-grey-d8fe574e3425d038c5d392ed9342517a9494391.svg"
        : $"https://image.tmdb.org/t/p/w185{ProfilePath}";
}

public class TmdbPersonSearchResponse
{
    [JsonPropertyName("results")]
    public List<TmdbPersonSearchItem>? Results { get; set; }
}

public class TmdbPersonSearchItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }

    public string FullProfileUrl => string.IsNullOrEmpty(ProfilePath)
        ? "https://www.themoviedb.org/assets/2/v4/glyphicons/basic/glyphicons-basic-4-user-grey-d8fe574e3425d038c5d392ed9342517a9494391.svg"
        : $"https://image.tmdb.org/t/p/w185{ProfilePath}";
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

public class TmdbImages
{
    [JsonPropertyName("backdrops")]
    public List<TmdbImage> Backdrops { get; set; } = new();
}

public class TmdbImage
{
    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = "";
}

public class TmdbVideosResponse
{
    [JsonPropertyName("results")]
    public List<TmdbVideo> Results { get; set; } = new();
}

public class TmdbVideo
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";
    
    [JsonPropertyName("site")]
    public string Site { get; set; } = "";
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}
// ── Split storage DTOs ───────────────────────────────────────────────────────

/// <summary>
/// Slim record stored in the hot "my_movie_list_slim" key.
/// Contains everything needed to render lists, filters, and sorting.
/// </summary>
public class WatchlistItemSlim
{
    public string ImdbId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string TitleType { get; set; } = null!;
    public string Year { get; set; } = null!;
    public string Genres { get; set; } = null!;
    public string? Director { get; set; }
    public string? PosterPath { get; set; }
    public int ParsedYear { get; set; }
    public WatchlistStatus Status { get; set; } = WatchlistStatus.Pending;
    public int? CurrentSeason { get; set; }
    public int? CurrentEpisode { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.Now;
    public int? UserRating { get; set; }
    public int? Rating20 { get; set; }
    public int? TmdbId { get; set; }
    public int? Runtime { get; set; }
    public TmdbCollection? Collection { get; set; }
}

public class AdvancedFilterState
{
    public bool IsActive { get; set; }
    public HashSet<string> IncludedGenres { get; set; } = new();
    public HashSet<string> ExcludedGenres { get; set; } = new();
    public GenreLogic GenreLogic { get; set; } = GenreLogic.Any;
    public int? SelectedPersonId { get; set; }
    public string? SelectedPersonName { get; set; }
    public HashSet<int> PersonMovieIds { get; set; } = new();
    public int? MinYear { get; set; }
    public int? MaxYear { get; set; }
    public int? MinRuntime { get; set; }
    public int? MaxRuntime { get; set; }
    public int? MinUserRating { get; set; }
    public int? MaxUserRating { get; set; }
    public double? MinTmdbRating { get; set; }
    public double? MaxTmdbRating { get; set; }
    public bool UnratedOnly { get; set; }
    public bool ShortFilmsOnly { get; set; }
}

/// <summary>
/// Heavy details stored in the cold "my_movie_details" key.
/// Only loaded when a modal is opened.
/// </summary>
public class WatchlistItemDetails
{
    public string ImdbId { get; set; } = null!;
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public double? VoteAverage { get; set; }
}

public class TmdbCollection
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("parts")]
    public List<TmdbMovie>? Parts { get; set; }

    public string FullPosterUrl => string.IsNullOrEmpty(PosterPath)
        ? "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=" 
        : $"https://image.tmdb.org/t/p/w500{PosterPath}";
}
