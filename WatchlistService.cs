using MyPrivateWatchlist.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace MyPrivateWatchlist.Services;

public class WatchlistService
{
    private readonly HttpClient _http;
    private readonly LocalStorageService _storage;
    private readonly IConfiguration _config;
    private readonly IJSRuntime _js;
    private readonly Dictionary<string, TmdbMovie> _movieCache = new();
    private readonly Dictionary<string, TmdbMovie> _movieDetailsByIdCache = new();
    private Dictionary<int, string> _genreMap = new();
    private CancellationTokenSource? _saveDebounceCts;
    private CancellationTokenSource? _searchDebounceCts;
    private readonly TimeSpan _searchDebounceDelay = TimeSpan.FromMilliseconds(220);
    private Task? _initializeTask;
    private bool _genreMapLoadStarted;
    public bool IsInitializing { get; private set; } = true;
    public string? ToastMessage { get; private set; }

    public async Task ShowToastAsync(string message, int durationMs = 3000)
    {
        ToastMessage = message;
        NotifyStateChanged();
        await Task.Delay(durationMs);
        if (ToastMessage == message)
        {
            ToastMessage = null;
            NotifyStateChanged();
        }
    }

    public List<WatchlistItem> Items { get; set; } = new();
    
    private List<WatchlistItem> _watchingCached = new();
    private List<WatchlistItem> _watchedCached = new();
    private List<WatchlistItem> _filteredCached = new();
    private List<WatchlistItem> _filteredWatchedCached = new();
    
    // Global Modal State
    public WatchlistItem? SelectedItem { get; private set; }
    public TmdbMovie? SelectedMovie { get; private set; }
    public bool IsModalOpen { get; private set; }
    public bool IsLoadingDetails { get; private set; }

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
        set
        {
            _searchQuery = value;
            DebounceSearchRefresh();
        }
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

    private int _filterMaxRating20 = 100;
    public int FilterMaxRating20 
    { 
        get => _filterMaxRating20; 
        set { _filterMaxRating20 = value; NotifyStateChanged(); } 
    }

    private RatingSystem _ratingSystem = RatingSystem.HundredPoint;
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
        int sYear = 0, eYear = 0;
        bool hasStart = int.TryParse(StartYear, out sYear);
        bool hasEnd = int.TryParse(EndYear, out eYear);
        bool checkType = SelectedType != "All";
        bool checkGenre = SelectedGenre != "All";
        bool checkSearch = !string.IsNullOrWhiteSpace(SearchQuery);

        var pendingFiltered = new List<WatchlistItem>();
        var watchedFiltered = new List<WatchlistItem>();
        var watching = new List<WatchlistItem>();

        foreach (var item in Items)
        {
            if (item.Status == WatchlistStatus.Watching)
            {
                watching.Add(item);
            }

            if (checkType && !item.TitleType.Equals(SelectedType, StringComparison.OrdinalIgnoreCase))
                continue;
            if (checkGenre && !item.Genres.Contains(SelectedGenre, StringComparison.OrdinalIgnoreCase))
                continue;
            if (hasStart && item.ParsedYear < sYear)
                continue;
            if (hasEnd && item.ParsedYear > eYear)
                continue;
            if (checkSearch &&
                !item.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) &&
                !(item.Director != null && item.Director.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (item.Status == WatchlistStatus.Pending)
            {
                pendingFiltered.Add(item);
            }
            else if (item.Status == WatchlistStatus.Watched)
            {
                var rating = item.Rating20 ?? 0;
                if (rating >= FilterMinRating20 && rating <= FilterMaxRating20)
                    watchedFiltered.Add(item);
            }
        }

        _watchingCached = watching;

        _filteredCached = (SortColumn switch
        {
            "Year" => SortDescending ? pendingFiltered.OrderByDescending(m => m.ParsedYear) : pendingFiltered.OrderBy(m => m.ParsedYear),
            "Type" => SortDescending ? pendingFiltered.OrderByDescending(m => m.TitleType) : pendingFiltered.OrderBy(m => m.TitleType),
            "DateAdded" => SortDescending ? pendingFiltered.OrderByDescending(m => m.DateAdded) : pendingFiltered.OrderBy(m => m.DateAdded),
            _ => SortDescending ? pendingFiltered.OrderByDescending(m => m.Title) : pendingFiltered.OrderBy(m => m.Title)
        }).ToList();

        _filteredWatchedCached = (WatchedSortColumn switch
        {
            "Rating" => WatchedSortDescending ? watchedFiltered.OrderByDescending(m => m.Rating20 ?? 0) : watchedFiltered.OrderBy(m => m.Rating20 ?? 0),
            "Year" => WatchedSortDescending ? watchedFiltered.OrderByDescending(m => m.ParsedYear) : watchedFiltered.OrderBy(m => m.ParsedYear),
            "Type" => WatchedSortDescending ? watchedFiltered.OrderByDescending(m => m.TitleType) : watchedFiltered.OrderBy(m => m.TitleType),
            "DateAdded" => WatchedSortDescending ? watchedFiltered.OrderByDescending(m => m.DateAdded) : watchedFiltered.OrderBy(m => m.DateAdded),
            _ => WatchedSortDescending ? watchedFiltered.OrderByDescending(m => m.Title) : watchedFiltered.OrderBy(m => m.Title)
        }).ToList();
    }

    public WatchlistService(HttpClient http, LocalStorageService storage, IConfiguration config, IJSRuntime js)
    {
        _http = http;
        _storage = storage;
        _config = config;
        _js = js;
    }

    public async Task InitializeAsync()
    {
        if (_initializeTask != null)
        {
            await _initializeTask;
            return;
        }

        _initializeTask = InitializeCoreAsync();
        await _initializeTask;
    }

    private async Task InitializeCoreAsync()
    {
        try
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

            RefreshCalculatedLists();
            OnStateChanged?.Invoke();
            StartGenreMapLoadIfNeeded();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Initialization error: {ex.Message}");
        }
        finally
        {
            IsInitializing = false;
            OnStateChanged?.Invoke();
        }
    }

    private void StartGenreMapLoadIfNeeded()
    {
        if (_genreMapLoadStarted)
        {
            return;
        }

        _genreMapLoadStarted = true;
        _ = LoadGenreMapAsync();
    }

    private async Task LoadGenreMapAsync()
    {
        await FetchGenreMapAsync();
        OnStateChanged?.Invoke();
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
        catch (Exception ex)
        {
            Console.WriteLine($"Genre map fetch error: {ex.Message}");
        }
    }

    public string GetGenreNames(List<int>? ids)
    {
        if (ids == null || !ids.Any()) return "";
        return string.Join(", ", ids.Select(id => _genreMap.TryGetValue(id, out var name) ? name : "").Where(n => !string.IsNullOrEmpty(n)));
    }

    public async Task UpdateListAsync(List<WatchlistItem> newList)
    {
        Items = newList;
        ScheduleSave();
        NotifyStateChanged();
    }

    private async Task SaveNowAsync()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        _saveDebounceCts = null;
        await _storage.SaveListAsync("my_movie_list", Items);
    }

    private void ScheduleSave()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;
        _ = DebouncedSaveAsync(cts.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(1000, token);
            await _storage.SaveListAsync("my_movie_list", Items);
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer save supersedes this one.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save error: {ex.Message}");
        }
    }

    private void DebounceSearchRefresh()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebouncedSearchRefreshAsync(cts.Token);
    }

    private async Task DebouncedSearchRefreshAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_searchDebounceDelay, token);
            NotifyStateChanged();
        }
        catch (OperationCanceledException)
        {
            // Expected while user is still typing.
        }
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
                    
                    var imagesUrl = $"https://api.themoviedb.org/3/movie/{movie.Id}/images?api_key={apiKey}";
                    var images = await _http.GetFromJsonAsync<TmdbImages>(imagesUrl);
                    if (images != null)
                    {
                        movie.BackdropPaths = images.Backdrops.Take(10).Select(b => b.FilePath).ToList();
                    }

                    var videosUrl = $"https://api.themoviedb.org/3/movie/{movie.Id}/videos?api_key={apiKey}";
                    var videos = await _http.GetFromJsonAsync<TmdbVideosResponse>(videosUrl);
                    movie.TrailerKey = videos?.Results?.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer")?.Key;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Details enrichment error for movie {movie.Id}: {ex.Message}");
                }

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
                    
                    var imagesUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/images?api_key={apiKey}";
                    var images = await _http.GetFromJsonAsync<TmdbImages>(imagesUrl);
                    if (images != null)
                    {
                        movie.BackdropPaths = images.Backdrops.Take(10).Select(b => b.FilePath).ToList();
                    }

                    var videosUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/videos?api_key={apiKey}";
                    var videos = await _http.GetFromJsonAsync<TmdbVideosResponse>(videosUrl);
                    movie.TrailerKey = videos?.Results?.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer")?.Key;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Details enrichment error for tv {tv.Id}: {ex.Message}");
                }

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
            var cacheKey = $"{mediaType}:{tmdbId}";
            if (_movieDetailsByIdCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

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
                        
                        var imagesUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}/images?api_key={apiKey}";
                        var images = await _http.GetFromJsonAsync<TmdbImages>(imagesUrl);
                        if (images != null)
                        {
                            movie.BackdropPaths = images.Backdrops.Take(10).Select(b => b.FilePath).ToList();
                        }

                        var videosUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}/videos?api_key={apiKey}";
                        var videos = await _http.GetFromJsonAsync<TmdbVideosResponse>(videosUrl);
                        movie.TrailerKey = videos?.Results?.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer")?.Key;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Details enrichment error for movie {tmdbId}: {ex.Message}");
                    }
                    _movieDetailsByIdCache[cacheKey] = movie;
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
                        
                        var imagesUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/images?api_key={apiKey}";
                        var images = await _http.GetFromJsonAsync<TmdbImages>(imagesUrl);
                        if (images != null)
                        {
                            movie.BackdropPaths = images.Backdrops.Take(10).Select(b => b.FilePath).ToList();
                        }

                        var videosUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/videos?api_key={apiKey}";
                        var videos = await _http.GetFromJsonAsync<TmdbVideosResponse>(videosUrl);
                        movie.TrailerKey = videos?.Results?.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer")?.Key;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Details enrichment error for tv {tv.Id}: {ex.Message}");
                    }
                    _movieDetailsByIdCache[cacheKey] = movie;
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
        catch (Exception ex)
        {
            Console.WriteLine($"Resolve IMDb error for '{title}': {ex.Message}");
        }
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
        }
    }

    public async Task ClearAllDataAsync()
    {
        Items.Clear();
        await _storage.SaveListAsync("my_movie_list", Items);
        NotifyStateChanged();
    }

    public async Task UpdateRatingAsync(WatchlistItem item, int? rating100)
    {
        var existing = Items.FirstOrDefault(i => i.ImdbId == item.ImdbId);
        if (existing != null)
        {
            existing.Rating20 = rating100;
            // Sync legacy field for backward compatibility (0-10)
            existing.UserRating = rating100.HasValue ? (int)Math.Round(rating100.Value / 10.0) : null;
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

    public async Task AddItemAsync(WatchlistItem item)
    {
        if (!Items.Any(i => i.ImdbId == item.ImdbId))
        {
            if (item.DateAdded == default) item.DateAdded = DateTime.Now;
            Items.Add(item);
            await UpdateListAsync(Items);
        }
    }

    public async Task AddToWatchlistAsync(WatchlistItem item)
    {
        await AddItemAsync(item);
    }

    public async Task RemoveItemAsync(WatchlistItem item)
    {
        var toRemove = Items.FirstOrDefault(i => i.ImdbId == item.ImdbId);
        if (toRemove != null)
        {
            Items.Remove(toRemove);
            await UpdateListAsync(Items);
        }
    }

    public async Task RemoveFromWatchlistAsync(WatchlistItem item)
    {
        await RemoveItemAsync(item);
    }

    public async Task UpdateItemAsync(WatchlistItem item)
    {
        var existing = Items.FirstOrDefault(i => i.ImdbId == item.ImdbId);
        if (existing != null)
        {
            existing.Status = item.Status;
            existing.CurrentSeason = item.CurrentSeason;
            existing.CurrentEpisode = item.CurrentEpisode;
            existing.UserRating = item.UserRating;
            existing.Rating20 = item.Rating20;
            await UpdateListAsync(Items);
        }
    }

    public async Task RemoveFromWatchlistByImdbIdAsync(string imdbId)
    {
        var toRemove = Items.FirstOrDefault(i => i.ImdbId == imdbId);
        if (toRemove != null)
        {
            Items.Remove(toRemove);
            await UpdateListAsync(Items);
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

    // Modal Control
    public async Task ShowDetailsAsync(WatchlistItem item)
    {
        SelectedItem = item;
        SelectedMovie = new TmdbMovie 
        { 
            Title = item.Title, 
            OriginalTitle = item.OriginalTitle,
            ReleaseDate = item.Year,
            Overview = item.Overview ?? ""
        };
        IsModalOpen = true;
        IsLoadingDetails = true;
        NotifyStateChanged(fullRefresh: false);

        var details = await GetTmdbDetailsAsync(item.ImdbId);
        if (details != null && SelectedItem == item)
        {
            SelectedMovie = details;
            
            // Persist the overview if this is a real item in our collection
            if (!string.IsNullOrEmpty(details.Overview) && Items.Any(i => i.ImdbId == item.ImdbId))
            {
                var listItem = Items.First(i => i.ImdbId == item.ImdbId);
                if (listItem.Overview != details.Overview)
                {
                    listItem.Overview = details.Overview;
                    await UpdateListAsync(Items);
                }
            }
            
            IsLoadingDetails = false;
            NotifyStateChanged(fullRefresh: false);
        }
    }

    public async Task ShowDetailsAsync(TmdbSearchResultItem searchItem)
    {
        var tempItem = new WatchlistItem
        {
            ImdbId = "",
            Title = searchItem.DisplayTitle,
            OriginalTitle = searchItem.DisplayOriginalTitle,
            Year = searchItem.DisplayDate,
            TitleType = searchItem.MediaType == "movie" ? "Movie" : "TV Series"
        };
        
        SelectedItem = tempItem;
        SelectedMovie = new TmdbMovie 
        { 
            Title = tempItem.Title, 
            OriginalTitle = tempItem.OriginalTitle,
            ReleaseDate = tempItem.Year,
            PosterPath = searchItem.PosterPath
        };
        IsModalOpen = true;
        IsLoadingDetails = true;
        NotifyStateChanged(fullRefresh: false);

        var details = await GetTmdbDetailsByIdAsync(searchItem.Id, searchItem.MediaType);
        if (details != null && SelectedItem == tempItem)
        {
            SelectedMovie = details;
            SelectedItem.ImdbId = details.ImdbId ?? "";
            IsLoadingDetails = false;
            NotifyStateChanged(fullRefresh: false);
        }
    }

    public async Task ShowRandomMovieAsync()
    {
        var pool = FilteredItems.Any() ? FilteredItems.ToList() : Items.Where(i => i.Status == WatchlistStatus.Pending).ToList();
        if (pool.Any())
        {
            var random = pool[Random.Shared.Next(pool.Count)];
            await ShowDetailsAsync(random);
        }
        else
        {
            _ = ShowToastAsync("Your library is currently empty! Please add some movies to your watchlist first.", 3500);
        }
    }

    public void CloseModal()
    {
        IsModalOpen = false;
        SelectedItem = null;
        SelectedMovie = null;
        NotifyStateChanged(fullRefresh: false);
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
