using MyPrivateWatchlist.Models;
using System.Net.Http.Json;

namespace MyPrivateWatchlist.Services;

public class WatchlistService
{
    private readonly HttpClient _http;
    private readonly LocalStorageService _storage;
    private readonly IConfiguration _config;
    private readonly Dictionary<string, TmdbMovie> _movieCache = new();

    public List<WatchlistItem> Items { get; private set; } = new();

    public string SelectedType { get; set; } = "All";
    public string SelectedGenre { get; set; } = "All";
    public string StartYear { get; set; } = "";
    public string EndYear { get; set; } = "";

    public WatchlistService(HttpClient http, LocalStorageService storage, IConfiguration config)
    {
        _http = http;
        _storage = storage;
        _config = config;
    }

    public async Task InitializeAsync()
    {
        var saved = await _storage.GetListAsync<WatchlistItem>("my_movie_list");
        if (saved != null) 
        {
            foreach (var item in saved)
            {
                if (item.ParsedYear == 0 && !string.IsNullOrEmpty(item.Year))
                {
                    var yearDigits = new string(item.Year.TakeWhile(char.IsDigit).ToArray());
                    int.TryParse(yearDigits, out int parsed);
                    item.ParsedYear = parsed;
                }
            }
            Items = saved;
        }
    }

    public async Task UpdateListAsync(List<WatchlistItem> newList)
    {
        Items = newList;
        await _storage.SaveListAsync("my_movie_list", Items);
    }

    public IEnumerable<WatchlistItem> FilteredItems
    {
        get
        {
            int sYear = 0, eYear = 0;
            bool hasStart = int.TryParse(StartYear, out sYear);
            bool hasEnd = int.TryParse(EndYear, out eYear);
            bool checkType = SelectedType != "All";
            bool checkGenre = SelectedGenre != "All";

            return Items.Where(m =>
            {
                if (checkType && !m.TitleType.Equals(SelectedType, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (checkGenre && !m.Genres.Contains(SelectedGenre, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (hasStart && m.ParsedYear < sYear)
                    return false;

                if (hasEnd && m.ParsedYear > eYear)
                    return false;

                return true;
            });
        }
    }

    public async Task<TmdbMovie?> GetTmdbDetailsAsync(string imdbId)
    {
        if (_movieCache.TryGetValue(imdbId, out var cachedMovie))
        {
            return cachedMovie;
        }

        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/find/{imdbId}?api_key={apiKey}&external_source=imdb_id";

        try
        {
            var response = await _http.GetFromJsonAsync<TmdbFindResult>(url);
            
            if (response?.MovieResults?.Any() == true) 
            {
                var movie = response.MovieResults.First();
                try 
                {
                    var creditsUrl = $"https://api.themoviedb.org/3/movie/{movie.Id}/credits?api_key={apiKey}";
                    var credits = await _http.GetFromJsonAsync<TmdbCredits>(creditsUrl);
                    if (credits != null)
                    {
                        movie.Directors = credits.Crew.Where(c => c.Job == "Director").Select(c => c.Name).Distinct().ToList();
                        movie.Actors = credits.Cast.Take(5).Select(c => c.Name).ToList();
                    }
                } catch { } 

                _movieCache[imdbId] = movie;
                return movie;
            }
            
            if (response?.TvResults?.Any() == true)
            {
                var tv = response.TvResults.First();
                var movie = new TmdbMovie
                {
                    Id = tv.Id,
                    Title = tv.Name,
                    Overview = tv.Overview,
                    PosterPath = tv.PosterPath,
                    VoteAverage = tv.VoteAverage,
                    ReleaseDate = tv.FirstAirDate
                };

                try 
                {
                    var creditsUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/credits?api_key={apiKey}";
                    var credits = await _http.GetFromJsonAsync<TmdbCredits>(creditsUrl);
                    if (credits != null)
                    {
                        movie.Directors = credits.Crew.Where(c => c.Job == "Executive Producer" || c.Job == "Director").Select(c => c.Name).Distinct().ToList();
                        movie.Actors = credits.Cast.Take(5).Select(c => c.Name).ToList();
                    }
                } catch { } 

                _movieCache[imdbId] = movie;
                return movie;
            }
        }
        catch (Exception ex)
        { 
            Console.WriteLine($"API Error fetching {imdbId}: {ex.Message}");
        }
        return null;
    }

    public async Task<TmdbMovie?> GetTmdbDetailsByIdAsync(int tmdbId, string mediaType)
    {
        var apiKey = _config["TmdbApiKey"];
        try
        {
            if (mediaType == "movie")
            {
                var movieUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}?api_key={apiKey}";
                var movie = await _http.GetFromJsonAsync<TmdbMovie>(movieUrl);
                if (movie != null)
                {
                    try 
                    {
                        var extUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}/external_ids?api_key={apiKey}";
                        var ext = await _http.GetFromJsonAsync<dynamic>(extUrl);
                        if (ext != null)
                        {
                            movie.ImdbId = ext.GetProperty("imdb_id").GetString();
                        }

                        var creditsUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}/credits?api_key={apiKey}";
                        var credits = await _http.GetFromJsonAsync<TmdbCredits>(creditsUrl);
                        if (credits != null)
                        {
                            movie.Directors = credits.Crew.Where(c => c.Job == "Director").Select(c => c.Name).Distinct().ToList();
                            movie.Actors = credits.Cast.Take(5).Select(c => c.Name).ToList();
                        }
                    } catch { } 
                    return movie;
                }
            }
            else if (mediaType == "tv")
            {
                var tvUrl = $"https://api.themoviedb.org/3/tv/{tmdbId}?api_key={apiKey}";
                var tv = await _http.GetFromJsonAsync<TmdbTvResult>(tvUrl);
                if (tv != null)
                {
                    var movie = new TmdbMovie
                    {
                        Id = tv.Id,
                        Title = tv.Name,
                        Overview = tv.Overview,
                        PosterPath = tv.PosterPath,
                        VoteAverage = tv.VoteAverage,
                        ReleaseDate = tv.FirstAirDate
                    };

                    try 
                    {
                        var extUrl = $"https://api.themoviedb.org/3/tv/{tmdbId}/external_ids?api_key={apiKey}";
                        var ext = await _http.GetFromJsonAsync<dynamic>(extUrl);
                        if (ext != null)
                        {
                            movie.ImdbId = ext.GetProperty("imdb_id").GetString();
                        }

                        var creditsUrl = $"https://api.themoviedb.org/3/tv/{tmdbId}/credits?api_key={apiKey}";
                        var credits = await _http.GetFromJsonAsync<TmdbCredits>(creditsUrl);
                        if (credits != null)
                        {
                            movie.Directors = credits.Crew.Where(c => c.Job == "Executive Producer" || c.Job == "Director").Select(c => c.Name).Distinct().ToList();
                            movie.Actors = credits.Cast.Take(5).Select(c => c.Name).ToList();
                        }
                    } catch { } 
                    return movie;
                }
            }
        }
        catch (Exception ex)
        { 
            Console.WriteLine($"API Error fetching TMDB ID {tmdbId}: {ex.Message}");
        }
        return null;
    }

    public async Task AddToWatchlistAsync(WatchlistItem item)
    {
        if (!Items.Any(i => i.ImdbId == item.ImdbId))
        {
            Items.Add(item);
            await UpdateListAsync(Items);
        }
    }

    public bool IsInWatchlist(string? imdbId) => !string.IsNullOrEmpty(imdbId) && Items.Any(i => i.ImdbId == imdbId);

    public bool IsInWatchlistFuzzy(string title, string? year)
    {
        return Items.Any(i => i.Title.Equals(title, StringComparison.OrdinalIgnoreCase) && 
                              (string.IsNullOrEmpty(year) || i.Year.Contains(year)));
    }
}