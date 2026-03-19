using MyPrivateWatchlist.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MyPrivateWatchlist.Services;

public class WatchlistService
{
    private readonly HttpClient _http;
    private readonly LocalStorageService _storage;
    private readonly IConfiguration _config;
    private readonly Dictionary<string, TmdbMovie> _movieCache = new();
    private Dictionary<int, string> _genreMap = new();
    private System.Threading.Timer? _saveTimer;

    public List<WatchlistItem> Items { get; private set; } = new();
    
    private List<WatchlistItem> _watchingCached = new();
    private List<WatchlistItem> _watchedCached = new();
    private List<WatchlistItem> _filteredCached = new();
    private List<WatchlistItem> _filteredWatchedCached = new();

    private string _selectedType = "All";
    public string SelectedType 
    { 
        get => _selectedType; 
        set { _selectedType = value; NotifyStateChanged(); } 
    }

    private string _selectedGenre = "All";
    public string SelectedGenre 
    { 
        get => _selectedGenre; 
        set { _selectedGenre = value; NotifyStateChanged(); } 
    }

    private string _startYear = "";
    public string StartYear 
    { 
        get => _startYear; 
        set { _startYear = value; NotifyStateChanged(); } 
    }

    private string _endYear = "";
    public string EndYear 
    { 
        get => _endYear; 
        set { _endYear = value; NotifyStateChanged(); } 
    }

    private string _searchQuery = "";
    public string SearchQuery 
    { 
        get => _searchQuery; 
        set { _searchQuery = value; NotifyStateChanged(); } 
    }

    public string SortColumn { get; set; } = "DateAdded";
    public bool SortDescending { get; set; } = true;

    public string WatchedSortColumn { get; set; } = "DateAdded";
    public bool WatchedSortDescending { get; set; } = true;

    private int _filterMinRating20 = 0;
    public int FilterMinRating20 
    { 
        get => _filterMinRating20; 
        set { _filterMinRating20 = value; NotifyStateChanged(); } 
    }

    private int _filterMaxRating20 = 20;
    public int FilterMaxRating20 
    { 
        get => _filterMaxRating20; 
        set { _filterMaxRating20 = value; NotifyStateChanged(); } 
    }

    private RatingSystem _ratingSystem = RatingSystem.TenPoint;
    public RatingSystem RatingSystem 
    { 
        get => _ratingSystem; 
        set { _ratingSystem = value; _ = _storage.SaveAsync("rating_system", value); NotifyStateChanged(); } 
    }

    public event Action? OnStateChanged;
    public void NotifyStateChanged(bool fullRefresh = true) 
    {
        if (fullRefresh) RefreshCalculatedLists();
        OnStateChanged?.Invoke();
    }
    
    private void RefreshWatchingCache()
    {
        _watchingCached = Items.Where(i => i.Status == WatchlistStatus.Watching).ToList();
    }

    private void RefreshCalculatedLists()
    {
        RefreshWatchingCache();
        
        // --- Shared Filter Base ---
        int sYear = 0, eYear = 0;
        bool hasStart = int.TryParse(StartYear, out sYear);
        bool hasEnd = int.TryParse(EndYear, out eYear);
        bool checkType = SelectedType != "All";
        bool checkGenre = SelectedGenre != "All";
        bool checkSearch = !string.IsNullOrWhiteSpace(SearchQuery);

        Func<WatchlistItem, bool> filterPredicate = m =>
        {
            if (checkType && !m.TitleType.Equals(SelectedType, StringComparison.OrdinalIgnoreCase))
                return false;
            if (checkGenre && !m.Genres.Contains(SelectedGenre, StringComparison.OrdinalIgnoreCase))
                return false;
            if (hasStart && m.ParsedYear < sYear)
                return false;
            if (hasEnd && m.ParsedYear > eYear)
                return false;
            if (checkSearch && !m.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) && 
                !(m.Director != null && m.Director.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        };

        // --- Calculate Filtered Pending (Watchlist) ---
        var pendingQuery = Items.Where(m => m.Status == WatchlistStatus.Pending).Where(filterPredicate);
        _filteredCached = (SortColumn switch
        {
            "Year" => SortDescending ? pendingQuery.OrderByDescending(m => m.ParsedYear) : pendingQuery.OrderBy(m => m.ParsedYear),
            "Type" => SortDescending ? pendingQuery.OrderByDescending(m => m.TitleType) : pendingQuery.OrderBy(m => m.TitleType),
            "DateAdded" => SortDescending ? pendingQuery.OrderByDescending(m => m.DateAdded) : pendingQuery.OrderBy(m => m.DateAdded),
            _ => SortDescending ? pendingQuery.OrderByDescending(m => m.Title) : pendingQuery.OrderBy(m => m.Title)
        }).ToList();

        // --- Calculate Filtered Watched (History) ---
        var watchedQuery = Items.Where(m => m.Status == WatchlistStatus.Watched)
                                .Where(filterPredicate)
                                .Where(m => (m.Rating20 ?? 0) >= FilterMinRating20 && (m.Rating20 ?? 0) <= FilterMaxRating20);
        _filteredWatchedCached = (WatchedSortColumn switch
        {
            "Rating" => WatchedSortDescending ? watchedQuery.OrderByDescending(m => m.Rating20 ?? 0) : watchedQuery.OrderBy(m => m.Rating20 ?? 0),
            "Year" => WatchedSortDescending ? watchedQuery.OrderByDescending(m => m.ParsedYear) : watchedQuery.OrderBy(m => m.ParsedYear),
            "Type" => WatchedSortDescending ? watchedQuery.OrderByDescending(m => m.TitleType) : watchedQuery.OrderBy(m => m.TitleType),
            "DateAdded" => WatchedSortDescending ? watchedQuery.OrderByDescending(m => m.DateAdded) : watchedQuery.OrderBy(m => m.DateAdded),
            _ => WatchedSortDescending ? watchedQuery.OrderByDescending(m => m.Title) : watchedQuery.OrderBy(m => m.Title)
        }).ToList();
    }

    public WatchlistService(HttpClient http, LocalStorageService storage, IConfiguration config)
    {
        _http = http;
        _storage = storage;
        _config = config;
    }

    public async Task InitializeAsync()
    {
        _ratingSystem = await _storage.GetAsync<RatingSystem>("rating_system");
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

                // Data Migration: UserRating (1-10) -> Rating20 (2-20)
                if (item.Rating20 == null && item.UserRating != null)
                {
                    item.Rating20 = item.UserRating * 2;
                }
            }
            Items = saved;
        }

        await FetchGenreMapAsync();
        RefreshCalculatedLists();
    }

    private async Task FetchGenreMapAsync()
    {
        var apiKey = _config["TmdbApiKey"];
        try
        {
            var movieGenres = await _http.GetFromJsonAsync<TmdbGenreListResponse>($"https://api.themoviedb.org/3/genre/movie/list?api_key={apiKey}");
            var tvGenres = await _http.GetFromJsonAsync<TmdbGenreListResponse>($"https://api.themoviedb.org/3/genre/tv/list?api_key={apiKey}");

            if (movieGenres != null)
                foreach (var g in movieGenres.Genres) _genreMap[g.Id] = g.Name;
            
            if (tvGenres != null)
                foreach (var g in tvGenres.Genres) _genreMap[g.Id] = g.Name;
        }
        catch { }
    }

    public string GetGenreNames(List<int>? ids)
    {
        if (ids == null || !ids.Any()) return "";
        return string.Join(", ", ids.Select(id => _genreMap.TryGetValue(id, out var name) ? name : "").Where(n => !string.IsNullOrEmpty(n)));
    }

    public async Task UpdateListAsync(List<WatchlistItem> newList)
    {
        Items = newList;
        await SaveNowAsync();
        NotifyStateChanged();
    }

    private async Task SaveNowAsync()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        await _storage.SaveListAsync("my_movie_list", Items);
    }

    private void ScheduleSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(async _ => 
        {
            await _storage.SaveListAsync("my_movie_list", Items);
        }, null, 1000, System.Threading.Timeout.Infinite);
    }

    public IEnumerable<WatchlistItem> WatchingItems => _watchingCached;
    public IEnumerable<WatchlistItem> WatchedItems => _filteredWatchedCached; // Now using filtered cache!
    public IEnumerable<WatchlistItem> FilteredItems => _filteredCached;


    public void ToggleSort(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }
        NotifyStateChanged();
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
                    OriginalTitle = tv.OriginalName,
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
                        var ext = await _http.GetFromJsonAsync<System.Text.Json.JsonElement>(extUrl);
                        if (ext.ValueKind != System.Text.Json.JsonValueKind.Undefined && ext.TryGetProperty("imdb_id", out var imdbProp))
                        {
                            movie.ImdbId = imdbProp.GetString();
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
                        OriginalTitle = tv.OriginalName,
                        Overview = tv.Overview,
                        PosterPath = tv.PosterPath,
                        VoteAverage = tv.VoteAverage,
                        ReleaseDate = tv.FirstAirDate
                    };

                    try 
                    {
                        var extUrl = $"https://api.themoviedb.org/3/tv/{tmdbId}/external_ids?api_key={apiKey}";
                        var ext = await _http.GetFromJsonAsync<System.Text.Json.JsonElement>(extUrl);
                        if (ext.ValueKind != System.Text.Json.JsonValueKind.Undefined && ext.TryGetProperty("imdb_id", out var imdbProp))
                        {
                            movie.ImdbId = imdbProp.GetString();
                        }

                        var creditsUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/credits?api_key={apiKey}";
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

    public async Task<string?> ResolveImdbIdAsync(string title, int? year)
    {
        var apiKey = _config["TmdbApiKey"];
        var query = System.Uri.EscapeDataString(title);
        var url = $"https://api.themoviedb.org/3/search/multi?api_key={apiKey}&query={query}";
        if (year.HasValue) url += $"&year={year.Value}&first_air_date_year={year.Value}";

        try
        {
            var searchResult = await _http.GetFromJsonAsync<TmdbSearchResult>(url);
            var first = searchResult?.Results?.FirstOrDefault(r => r.MediaType == "movie" || r.MediaType == "tv");
            
            if (first != null)
            {
                var extUrl = $"https://api.themoviedb.org/3/{first.MediaType}/{first.Id}/external_ids?api_key={apiKey}";
                var extIds = await _http.GetFromJsonAsync<TmdbExternalIds>(extUrl);
                return extIds?.ImdbId;
            }
        }
        catch { }
        return null;
    }

    public async Task UpdateStatusAsync(WatchlistItem item, WatchlistStatus status)
    {
        var existing = Items.FirstOrDefault(i => i.ImdbId == item.ImdbId);
        if (existing != null)
        {
            existing.Status = status;
            if (status == WatchlistStatus.Watching && existing.TitleType.Contains("TV", StringComparison.OrdinalIgnoreCase))
            {
                existing.CurrentSeason ??= 1;
                existing.CurrentEpisode ??= 1;
            }
            await UpdateListAsync(Items);
            NotifyStateChanged();
        }
    }

    public async Task ClearAllDataAsync()
    {
        Items.Clear();
        await _storage.SaveListAsync("watchlist_data", Items);
        RefreshCalculatedLists();
        NotifyStateChanged();
    }

    public async Task UpdateRatingAsync(WatchlistItem item, int? rating20)
    {
        var existing = Items.FirstOrDefault(i => i.ImdbId == item.ImdbId);
        if (existing != null)
        {
            existing.Rating20 = rating20;
            // Sync legacy field for backward compatibility
            existing.UserRating = rating20.HasValue ? (int)Math.Round(rating20.Value / 2.0) : null;
            await UpdateListAsync(Items);
        }
    }

    public async Task UpdateProgressAsync(WatchlistItem item, int? season, int? episode)
    {
        var existing = Items.FirstOrDefault(i => i.ImdbId == item.ImdbId);
        if (existing != null)
        {
            existing.CurrentSeason = season;
            existing.CurrentEpisode = episode;
            
            // Fast refresh: only internal counter and Watching list
            RefreshWatchingCache();
            NotifyStateChanged(fullRefresh: false);
            
            // Debounced save to disk
            ScheduleSave();
        }
    }

    public async Task AddToWatchlistAsync(WatchlistItem item)
    {
        if (!Items.Any(i => i.ImdbId == item.ImdbId))
        {
            if (item.DateAdded == default) item.DateAdded = DateTime.Now;
            Items.Add(item);
            await UpdateListAsync(Items);
            NotifyStateChanged();
        }
    }

    public async Task RemoveFromWatchlistAsync(WatchlistItem item)
    {
        var toRemove = Items.FirstOrDefault(i => i.ImdbId == item.ImdbId);
        if (toRemove != null)
        {
            Items.Remove(toRemove);
            await UpdateListAsync(Items);
            NotifyStateChanged();
        }
    }

    public async Task RemoveFromWatchlistByImdbIdAsync(string imdbId)
    {
        var toRemove = Items.FirstOrDefault(i => i.ImdbId == imdbId);
        if (toRemove != null)
        {
            Items.Remove(toRemove);
            await UpdateListAsync(Items);
            NotifyStateChanged();
        }
    }

    public bool IsInWatchlist(string? imdbId) => !string.IsNullOrEmpty(imdbId) && Items.Any(i => i.ImdbId == imdbId);

    public bool IsInWatchlistFuzzy(string title, string? year)
    {
        return Items.Any(i => i.Title.Equals(title, StringComparison.OrdinalIgnoreCase) && 
                              (string.IsNullOrEmpty(year) || i.Year.Contains(year)));
    }

    public void ToggleWatchedSort(string column)
    {
        if (WatchedSortColumn == column)
        {
            WatchedSortDescending = !WatchedSortDescending;
        }
        else
        {
            WatchedSortColumn = column;
            WatchedSortDescending = (column == "DateAdded" || column == "Year" || column == "Rating");
        }
        NotifyStateChanged();
    }
}

public class TmdbFindResult
{
    [JsonPropertyName("movie_results")]
    public List<TmdbMovie> MovieResults { get; set; } = new();
    [JsonPropertyName("tv_results")]
    public List<TmdbTvResult> TvResults { get; set; } = new();
}

public class TmdbSearchResult
{
    [JsonPropertyName("results")]
    public List<TmdbSearchItem> Results { get; set; } = new();
}

public class TmdbSearchItem
{
    public int Id { get; set; }
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }
    public string? Name { get; set; }
    public string? Title { get; set; }
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
}

public class TmdbExternalIds
{
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }
}