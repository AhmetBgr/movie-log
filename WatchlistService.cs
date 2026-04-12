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
    private readonly Microsoft.AspNetCore.Components.NavigationManager _nav;
    private readonly Dictionary<string, TmdbMovie> _movieCache = new();
    private readonly Dictionary<string, TmdbMovie> _movieDetailsByIdCache = new();
    private readonly Dictionary<int, TmdbCollection> _collectionCache = new();
    private readonly Dictionary<int, TmdbPerson> _personCache = new();
    private readonly Dictionary<int, TmdbPersonCombinedCredits> _personCreditsCache = new();
    private Dictionary<string, WatchlistItemDetails> _detailsStore = new();
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
    public List<CustomCollection> Collections { get; set; } = new();

    
    private List<WatchlistItem> _watchingCached = new();
    private List<WatchlistItem> _watchedCached = new();
    private List<WatchlistItem> _filteredCached = new();
    private List<WatchlistItem> _filteredWatchedCached = new();
    
    // Global Modal State
    public WatchlistItem? SelectedItem { get; private set; }
    public TmdbMovie? SelectedMovie { get; private set; }
    public bool IsModalOpen { get; private set; }
    public bool IsLoadingDetails { get; private set; }
    
    // Franchise Page State
    public TmdbCollection? SelectedCollection { get; private set; }
    public bool IsFranchisePageOpen { get; private set; }

    // Person Profile State
    public TmdbPerson? SelectedPerson { get; private set; }
    public TmdbPersonCombinedCredits? SelectedPersonCredits { get; private set; }
    public bool IsPersonProfileOpen { get; private set; }
    public bool IsLoadingPerson { get; private set; }

    // Side Panel State
    public bool IsSidePanelOpen { get; private set; }
    public bool UseSidePanel { get; set; } = true; // Toggle for side panel vs modal on desktop

    // Cached mobile state to avoid JS interop delay on first click
    private bool? _lastIsMobile = null;
    private bool _showDetailsInProgress = false;

    /// <summary>Seed the cached isMobile value from the layout on first render.</summary>
    public void PrimeIsMobileCache(bool isMobile) => _lastIsMobile ??= isMobile;

    public class TabFilterState
    {
        public string SearchQuery { get; set; } = "";
        public string SelectedType { get; set; } = "All";
        public string SelectedGenre { get; set; } = "All";
        public string StartYear { get; set; } = "";
        public string EndYear { get; set; } = "";
        public int FilterMinRating20 { get; set; } = 0;
        public int FilterMaxRating20 { get; set; } = 100;
        public double FilterMinVote { get; set; } = 0;
        public double FilterMaxVote { get; set; } = 10;
        public AdvancedFilterState AdvancedFilter { get; set; } = new() { IsActive = true };
        public string SortColumn { get; set; } = "DateAdded";
        public bool SortDescending { get; set; } = true;
    }

    private Dictionary<string, TabFilterState> _tabFilters = new()
    {
        { "watchlist", new TabFilterState() },
        { "watched", new TabFilterState() },
        { "watching", new TabFilterState() },
        { "collections", new TabFilterState() }
    };

    public string ActiveTab { get; set; } = "watchlist";
    public TabFilterState CurrentFilters => _tabFilters.TryGetValue(ActiveTab.ToLower(), out var f) ? f : _tabFilters["watchlist"];

    // Basic property redirects for compatibility with existing components
    public string SelectedType { get => CurrentFilters.SelectedType; set { CurrentFilters.SelectedType = value; NotifyStateChanged(); } }
    public string SelectedGenre { get => CurrentFilters.SelectedGenre; set { CurrentFilters.SelectedGenre = value; NotifyStateChanged(); } }
    public string StartYear { get => CurrentFilters.StartYear; set { CurrentFilters.StartYear = value; NotifyStateChanged(); } }
    public string EndYear { get => CurrentFilters.EndYear; set { CurrentFilters.EndYear = value; NotifyStateChanged(); } }
    public string SearchQuery { get => CurrentFilters.SearchQuery; set { CurrentFilters.SearchQuery = value; DebounceSearchRefresh(); } }
    public string SortColumn { get => CurrentFilters.SortColumn; set { CurrentFilters.SortColumn = value; NotifyStateChanged(); } }
    public bool SortDescending { get => CurrentFilters.SortDescending; set { CurrentFilters.SortDescending = value; NotifyStateChanged(); } }

    public int FilterMinRating20 { get => CurrentFilters.FilterMinRating20; set { CurrentFilters.FilterMinRating20 = value; NotifyStateChanged(); } }
    public int FilterMaxRating20 { get => CurrentFilters.FilterMaxRating20; set { CurrentFilters.FilterMaxRating20 = value; NotifyStateChanged(); } }
    public double FilterMinVote { get => CurrentFilters.FilterMinVote; set { CurrentFilters.FilterMinVote = value; NotifyStateChanged(); } }
    public double FilterMaxVote { get => CurrentFilters.FilterMaxVote; set { CurrentFilters.FilterMaxVote = value; NotifyStateChanged(); } }
    public AdvancedFilterState AdvancedFilter { get => CurrentFilters.AdvancedFilter; set { CurrentFilters.AdvancedFilter = value; NotifyStateChanged(); } }

    // Legacy redirects for separate sort properties
    public string WatchedSortColumn { get => _tabFilters["watched"].SortColumn; set { _tabFilters["watched"].SortColumn = value; NotifyStateChanged(); } }
    public bool WatchedSortDescending { get => _tabFilters["watched"].SortDescending; set { _tabFilters["watched"].SortDescending = value; NotifyStateChanged(); } }
    public string WatchingSortColumn { get => _tabFilters["watching"].SortColumn; set { _tabFilters["watching"].SortColumn = value; NotifyStateChanged(); } }
    public bool WatchingSortDescending { get => _tabFilters["watching"].SortDescending; set { _tabFilters["watching"].SortDescending = value; NotifyStateChanged(); } }


    private RatingSystem _ratingSystem = RatingSystem.HundredPoint;
    public RatingSystem RatingSystem 
    { 
        get => _ratingSystem; 
        set { _ratingSystem = value; _ = _storage.SaveAsync("rating_system", value); NotifyStateChanged(); } 
    }

    private DataFetchPreference _fetchPreference = DataFetchPreference.OnDemand;
    public DataFetchPreference FetchPreference 
    { 
        get => _fetchPreference; 
        set { _fetchPreference = value; _ = _storage.SaveAsync("fetch_preference", value); NotifyStateChanged(); } 
    }

    private bool _enableSearchHistory = false;
    public bool EnableSearchHistory 
    { 
        get => _enableSearchHistory; 
        set { _enableSearchHistory = value; _ = _storage.SaveAsync("enable_search_history", value); if(!value) ClearSearchHistory(); NotifyStateChanged(); } 
    }

    private string _openSubtitlesApiKey = "";
    public string OpenSubtitlesApiKey 
    { 
        get => _openSubtitlesApiKey; 
        set { _openSubtitlesApiKey = value; _ = _storage.SaveAsync("opensubtitles_apikey", value); NotifyStateChanged(); } 
    }

    public List<string> SearchHistory { get; set; } = new();

    public void AddToSearchHistory(string query)
    {
        if (!EnableSearchHistory || string.IsNullOrWhiteSpace(query)) return;
        
        query = query.Trim();
        SearchHistory.RemoveAll(s => s.Equals(query, StringComparison.OrdinalIgnoreCase));
        SearchHistory.Insert(0, query);
        
        if (SearchHistory.Count > 10)
            SearchHistory = SearchHistory.Take(10).ToList();
            
        _ = _storage.SaveAsync("search_history", SearchHistory);
    }

    public void RemoveFromSearchHistory(string query)
    {
        SearchHistory.RemoveAll(s => s.Equals(query, StringComparison.OrdinalIgnoreCase));
        _ = _storage.SaveAsync("search_history", SearchHistory);
        NotifyStateChanged();
    }

    public void ClearSearchHistory()
    {
        SearchHistory.Clear();
        _ = _storage.SaveAsync("search_history", SearchHistory);
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
        var wl = _tabFilters["watchlist"];
        var wd = _tabFilters["watched"];
        var wg = _tabFilters["watching"];

        var pendingFiltered = new List<WatchlistItem>();
        var watchedFiltered = new List<WatchlistItem>();
        var watching = new List<WatchlistItem>();

        foreach (var item in Items)
        {
            if (item.Status == WatchlistStatus.Watching)
            {
                if (PassesFilters(item, wg)) watching.Add(item);
            }
            else if (item.Status == WatchlistStatus.Pending)
            {
                if (PassesFilters(item, wl)) pendingFiltered.Add(item);
            }
            else if (item.Status == WatchlistStatus.Watched)
            {
                if (PassesFilters(item, wd)) watchedFiltered.Add(item);
            }
        }

        _watchingCached = (wg.SortColumn switch
        {
            "Year" => wg.SortDescending ? watching.OrderByDescending(m => m.ParsedYear) : watching.OrderBy(m => m.ParsedYear),
            "Type" => wg.SortDescending ? watching.OrderByDescending(m => m.TitleType) : watching.OrderBy(m => m.TitleType),
            "DateAdded" => wg.SortDescending ? watching.OrderByDescending(m => m.DateAdded) : watching.OrderBy(m => m.DateAdded),
            _ => wg.SortDescending ? watching.OrderByDescending(m => m.Title) : watching.OrderBy(m => m.Title)
        }).ToList();

        _filteredCached = (wl.SortColumn switch
        {
            "Year" => wl.SortDescending ? pendingFiltered.OrderByDescending(m => m.ParsedYear) : pendingFiltered.OrderBy(m => m.ParsedYear),
            "Type" => wl.SortDescending ? pendingFiltered.OrderByDescending(m => m.TitleType) : pendingFiltered.OrderBy(m => m.TitleType),
            "DateAdded" => wl.SortDescending ? pendingFiltered.OrderByDescending(m => m.DateAdded) : pendingFiltered.OrderBy(m => m.DateAdded),
            _ => wl.SortDescending ? pendingFiltered.OrderByDescending(m => m.Title) : pendingFiltered.OrderBy(m => m.Title)
        }).ToList();

        _filteredWatchedCached = (wd.SortColumn switch
        {
            "Rating" => wd.SortDescending ? watchedFiltered.OrderByDescending(m => m.Rating20 ?? 0) : watchedFiltered.OrderBy(m => m.Rating20 ?? 0),
            "Year" => wd.SortDescending ? watchedFiltered.OrderByDescending(m => m.ParsedYear) : watchedFiltered.OrderBy(m => m.ParsedYear),
            "Type" => wd.SortDescending ? watchedFiltered.OrderByDescending(m => m.TitleType) : watchedFiltered.OrderBy(m => m.TitleType),
            "DateAdded" => wd.SortDescending ? watchedFiltered.OrderByDescending(m => m.DateAdded) : watchedFiltered.OrderBy(m => m.DateAdded),
            _ => wd.SortDescending ? watchedFiltered.OrderByDescending(m => m.Title) : watchedFiltered.OrderBy(m => m.Title)
        }).ToList();
    }

    private bool PassesFilters(WatchlistItem item, TabFilterState f)
    {
        int sYear = 0, eYear = 0;
        bool hasStart = int.TryParse(f.StartYear, out sYear);
        bool hasEnd = int.TryParse(f.EndYear, out eYear);
        bool checkType = f.SelectedType != "All";
        bool checkGenre = f.SelectedGenre != "All";
        bool checkSearch = !string.IsNullOrWhiteSpace(f.SearchQuery);

        if (checkType && !item.TitleType.Equals(f.SelectedType, StringComparison.OrdinalIgnoreCase))
            return false;
        if (checkGenre && (item.Genres == null || !item.Genres.Contains(f.SelectedGenre, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (hasStart && item.ParsedYear < sYear)
            return false;
        if (hasEnd && item.ParsedYear > eYear)
            return false;
        if (checkSearch && !item.Title.Contains(f.SearchQuery, StringComparison.OrdinalIgnoreCase))
            return false;

        // Custom Status specific logic
        if (item.Status == WatchlistStatus.Pending)
        {
            if (f.FilterMinVote > 0 || f.FilterMaxVote < 10)
            {
                if (!item.VoteAverage.HasValue) return false;
                if (item.VoteAverage.Value < f.FilterMinVote || item.VoteAverage.Value > f.FilterMaxVote) return false;
            }
        }
        else if (item.Status == WatchlistStatus.Watched)
        {
            var rating = item.Rating20 ?? 0;
            if (rating < f.FilterMinRating20 || rating > f.FilterMaxRating20) return false;
        }

        // Advanced Filtering
        if (f.AdvancedFilter.IsActive)
        {
            if (f.AdvancedFilter.IncludedGenres.Any())
            {
                var itemGenreList = (item.Genres ?? "").Split(',').Select(g => g.Trim()).ToList();
                if (f.AdvancedFilter.GenreLogic == GenreLogic.All)
                {
                    if (!f.AdvancedFilter.IncludedGenres.All(ig => itemGenreList.Contains(ig, StringComparer.OrdinalIgnoreCase)))
                        return false;
                }
                else
                {
                    if (!f.AdvancedFilter.IncludedGenres.Any(ig => itemGenreList.Contains(ig, StringComparer.OrdinalIgnoreCase)))
                        return false;
                }
            }

            if (f.AdvancedFilter.ExcludedGenres.Any())
            {
                var itemGenreList = (item.Genres ?? "").Split(',').Select(g => g.Trim()).ToList();
                if (f.AdvancedFilter.ExcludedGenres.Any(eg => itemGenreList.Contains(eg, StringComparer.OrdinalIgnoreCase)))
                    return false;
            }

            if (!string.IsNullOrEmpty(f.AdvancedFilter.TitleSearch) && !item.Title.Contains(f.AdvancedFilter.TitleSearch, StringComparison.OrdinalIgnoreCase))
                return false;

            if (f.AdvancedFilter.MinYear.HasValue && item.ParsedYear < f.AdvancedFilter.MinYear.Value) return false;
            if (f.AdvancedFilter.MaxYear.HasValue && item.ParsedYear > f.AdvancedFilter.MaxYear.Value) return false;

            if (f.AdvancedFilter.MinUserRating.HasValue && (item.Rating20 ?? 0) < f.AdvancedFilter.MinUserRating.Value) return false;
            if (f.AdvancedFilter.MaxUserRating.HasValue && (item.Rating20 ?? 100) > f.AdvancedFilter.MaxUserRating.Value) return false;

            if (f.AdvancedFilter.MinTmdbRating.HasValue && (item.VoteAverage ?? 0) < f.AdvancedFilter.MinTmdbRating.Value) return false;
            if (f.AdvancedFilter.MaxTmdbRating.HasValue && (item.VoteAverage ?? 10) > f.AdvancedFilter.MaxTmdbRating.Value) return false;

            if (f.AdvancedFilter.UnratedOnly && item.Rating20.HasValue) return false;
            if (f.AdvancedFilter.ShortFilmsOnly && (item.Runtime ?? 999) >= 85) return false;
        }

        return true;
    }

    public WatchlistService(HttpClient http, LocalStorageService storage, IConfiguration config, IJSRuntime js, Microsoft.AspNetCore.Components.NavigationManager nav)
    {
        _http = http;
        _storage = storage;
        _config = config;
        _js = js;
        _nav = nav;
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
        var _sw = System.Diagnostics.Stopwatch.StartNew();
        var _lap = System.Diagnostics.Stopwatch.StartNew();
        void LogStep(string label)
        {
            Console.WriteLine($"[INIT] {label,-45} {_lap.ElapsedMilliseconds,6} ms   (total: {_sw.ElapsedMilliseconds} ms)");
            _lap.Restart();
        }

        Console.WriteLine("[INIT] ── Loading started ──────────────────────────────");
        try
        {
            _ratingSystem = await _storage.GetAsync<RatingSystem>("rating_system");
            LogStep("GetAsync: rating_system");

            var savedSlim = await _storage.GetCompressedListAsync<WatchlistItemSlim>("my_movie_list_slim");
            LogStep($"GetCompressedListAsync: my_movie_list_slim ({savedSlim?.Count ?? 0} items)");

            var savedDetails = await _storage.GetCompressedAsync<Dictionary<string, WatchlistItemDetails>>("my_movie_details");
            _detailsStore = savedDetails ?? new();
            LogStep($"GetCompressedAsync: my_movie_details ({_detailsStore.Count} entries)");

            if (savedSlim != null && savedSlim.Count > 0)
            {
                Items = savedSlim.Select(s =>
                {
                    _detailsStore.TryGetValue(s.ImdbId, out var det);
                    var item = new WatchlistItem
                    {
                        ImdbId        = s.ImdbId,
                        Title         = s.Title,
                        TitleType     = s.TitleType,
                        Year          = s.Year,
                        Genres        = s.Genres,
                        Director      = s.Director,
                        PosterPath    = s.PosterPath,
                        ParsedYear    = s.ParsedYear,
                        Status        = s.Status,
                        CurrentSeason = s.CurrentSeason,
                        CurrentEpisode= s.CurrentEpisode,
                        DateAdded     = s.DateAdded,
                        UserRating    = s.UserRating,
                        Rating20      = s.Rating20,
                        TmdbId        = s.TmdbId,
                        Runtime       = s.Runtime,
                        OriginalTitle = det?.OriginalTitle,
                        Overview      = det?.Overview,
                        VoteAverage   = det?.VoteAverage,
                        Collection    = s.Collection
                    };
                    // ParsedYear migration
                    if (item.ParsedYear == 0 && !string.IsNullOrEmpty(item.Year))
                    {
                        var yearDigits = new string(item.Year.TakeWhile(char.IsDigit).ToArray());
                        int.TryParse(yearDigits, out int parsed);
                        item.ParsedYear = parsed;
                    }
                    // UserRating migration
                    if (item.Rating20 == null && item.UserRating != null)
                        item.Rating20 = item.UserRating * 2;
                    return item;
                }).ToList();
                LogStep("Reconstruct Items from slim + details");

                // Background Migration: Populate TmdbId and Runtime from details
                _ = Task.Run(async () => {
                    bool changed = false;
                    int migratedCount = 0;
                    foreach(var item in Items) {
                        if (migratedCount >= 40) break; // Limit per session to prevent API spam
                        if (!item.TmdbId.HasValue || !item.Runtime.HasValue) {
                            var d = await GetTmdbDetailsByImdbIdAsync(item.ImdbId);
                            if (d != null) {
                                item.TmdbId = d.Id;
                                item.Runtime = d.Runtime;
                                changed = true;
                                migratedCount++;
                                await Task.Delay(250); // Be nice to the TMDB API
                            }
                        }
                    }
                    if (changed) await PersistAsync();
                });
            }

            RefreshCalculatedLists();
            LogStep("RefreshCalculatedLists");

            OnStateChanged?.Invoke();
            LogStep("OnStateChanged (first render)");

            StartGenreMapLoadIfNeeded();
            LogStep("StartGenreMapLoadIfNeeded (fire-and-forget)");

            _fetchPreference = await _storage.GetAsync<DataFetchPreference>("fetch_preference");
            LogStep("GetAsync: fetch_preference");

            _enableSearchHistory = await _storage.GetAsync<bool>("enable_search_history");
            LogStep("GetAsync: enable_search_history");

            _openSubtitlesApiKey = await _storage.GetAsync<string>("opensubtitles_apikey") ?? "";
            LogStep("GetAsync: opensubtitles_apikey");

            SearchHistory = await _storage.GetAsync<List<string>>("search_history") ?? new();
            LogStep("GetAsync: search_history");

            Collections = await _storage.GetAsync<List<CustomCollection>>("my_custom_collections") ?? new();
            LogStep("GetAsync: my_custom_collections");

            _ = BackgroundHydrationLoop();

            LogStep("BackgroundHydrationLoop (fire-and-forget)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INIT] ERROR: {ex.Message}");
        }
        finally
        {
            IsInitializing = false;
            _sw.Stop();
            Console.WriteLine($"[INIT] ── Total init time: {_sw.ElapsedMilliseconds} ms ─────────────────");
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
        Console.WriteLine($"UpdateListAsync called with {newList.Count} items");
        Items = newList;
        Console.WriteLine("Items collection updated");
        ScheduleSave();
        Console.WriteLine("ScheduleSave called");
        NotifyStateChanged();
        Console.WriteLine("NotifyStateChanged called");
    }

    public async Task UpdateListAndCollectionsAsync(List<WatchlistItem> newList, List<CustomCollection> newCollections)
    {
        Console.WriteLine($"UpdateListAndCollectionsAsync called with {newList.Count} items, {newCollections?.Count ?? 0} collections");
        Items = newList ?? new();
        Collections = newCollections ?? new();
        ScheduleSave();
        await _storage.SaveAsync("my_custom_collections", Collections);
        NotifyStateChanged();
    }

    private async Task SaveNowAsync()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        _saveDebounceCts = null;
        await PersistAsync();
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
            await PersistAsync();
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
                var findMovie = response.MovieResults.First();
                var fullMovie = await GetTmdbDetailsByIdAsync(findMovie.Id, "movie");
                if (fullMovie != null)
                {
                    fullMovie.ImdbId ??= imdbId;
                    _movieCache[imdbId] = fullMovie;
                    return fullMovie;
                }
            }
            
            if (response?.TvResults?.Any() == true)
            {
                var tv = response.TvResults.First();
                var fullTv = await GetTmdbDetailsByIdAsync(tv.Id, "tv");
                if (fullTv != null)
                {
                    fullTv.ImdbId ??= imdbId;
                    _movieCache[imdbId] = fullTv;
                    return fullTv;
                }
            }
        }
        catch (Exception ex)
        { 
            Console.WriteLine($"API Error fetching {imdbId}: {ex.Message}");
        }
        return null;
    }

    public async Task<TmdbMovie?> GetTmdbBasicDetailsAsync(string imdbId)
    {
        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/find/{imdbId}?api_key={apiKey}&external_source=imdb_id";

        try
        {
            var response = await _http.GetFromJsonAsync<TmdbFindResult>(url);

            if (response?.MovieResults?.Any() == true)
            {
                var movie = response.MovieResults.First();
                movie.ImdbId ??= imdbId;
                return movie;
            }

            if (response?.TvResults?.Any() == true)
            {
                var tv = response.TvResults.First();
                return new TmdbMovie
                {
                    Id = tv.Id,
                    ImdbId = imdbId,
                    Title = tv.Name,
                    OriginalTitle = tv.OriginalName,
                    Overview = tv.Overview,
                    PosterPath = tv.PosterPath,
                    ReleaseDate = tv.FirstAirDate,
                    VoteAverage = tv.VoteAverage,
                    OriginalLanguage = tv.OriginalLanguage,
                    Status = tv.Status,
                    VoteCount = tv.VoteCount,
                    Popularity = tv.Popularity,
                    GenreList = tv.GenreList
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"API Error fetching basic details for {imdbId}: {ex.Message}");
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
                        var creditsResponse = await _http.GetFromJsonAsync<TmdbCredits>(creditsUrl);
                        if (creditsResponse != null)
                        {
                            movie.Credits = creditsResponse;
                        }
                        
                        var imagesUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}/images?api_key={apiKey}";
                        var images = await _http.GetFromJsonAsync<TmdbImages>(imagesUrl);
                        if (images != null)
                        {
                            // Filter for textless images (Iso6391 is null) to avoid title treatments and logos
                            var filtered = images.Backdrops
                                .Where(b => string.IsNullOrEmpty(b.Iso6391))
                                .OrderByDescending(b => b.VoteAverage)
                                .ToList();

                            if (!filtered.Any()) 
                                filtered = images.Backdrops.OrderByDescending(b => b.VoteAverage).ToList();

                            movie.BackdropPaths = filtered.Take(15).Select(b => b.FilePath).ToList();
                        }

                        var videosUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}/videos?api_key={apiKey}";
                        var videos = await _http.GetFromJsonAsync<TmdbVideosResponse>(videosUrl);
                        movie.TrailerKey = videos?.Results?.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer")?.Key;

                        var keywordsUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}/keywords?api_key={apiKey}";
                        var keywordResponse = await _http.GetFromJsonAsync<TmdbKeywordResponse>(keywordsUrl);
                        if (keywordResponse != null)
                        {
                            movie.Keywords = keywordResponse.AllKeywords
                                .Where(k => !string.IsNullOrWhiteSpace(k.Name))
                                .GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
                                .Select(g => g.First())
                                .OrderBy(k => k.Name)
                                .ToList();
                        }

                        var releaseDatesUrl = $"https://api.themoviedb.org/3/movie/{tmdbId}/release_dates?api_key={apiKey}";
                        var releaseDates = await _http.GetFromJsonAsync<TmdbMovieReleaseDatesResponse>(releaseDatesUrl);
                        if (releaseDates != null)
                        {
                            movie.Certifications = releaseDates.Results
                                .Select(region => new TmdbCertification
                                {
                                    Region = region.Region,
                                    Rating = region.ReleaseDates
                                        .Select(r => r.Certification?.Trim())
                                        .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)) ?? ""
                                })
                                .Where(c => !string.IsNullOrWhiteSpace(c.Region) && !string.IsNullOrWhiteSpace(c.Rating))
                                .OrderBy(c => c.Region)
                                .ToList();
                        }
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
                        ReleaseDate = tv.FirstAirDate,
                        GenreList = tv.GenreList,
                        Runtime = tv.EpisodeRunTime?.FirstOrDefault(),
                        OriginalLanguage = tv.OriginalLanguage,
                        Status = tv.Status,
                        VoteCount = tv.VoteCount,
                        Popularity = tv.Popularity
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
                        var creditsResponse = await _http.GetFromJsonAsync<TmdbCredits>(creditsUrl);
                        if (creditsResponse != null)
                        {
                            movie.Credits = creditsResponse;
                        }
                        
                        var imagesUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/images?api_key={apiKey}";
                        var images = await _http.GetFromJsonAsync<TmdbImages>(imagesUrl);
                        if (images != null)
                        {
                            // Filter for textless images (Iso6391 is null) to avoid title treatments and logos
                            var filtered = images.Backdrops
                                .Where(b => string.IsNullOrEmpty(b.Iso6391))
                                .OrderByDescending(b => b.VoteAverage)
                                .ToList();

                            if (!filtered.Any()) 
                                filtered = images.Backdrops.OrderByDescending(b => b.VoteAverage).ToList();

                            movie.BackdropPaths = filtered.Take(15).Select(b => b.FilePath).ToList();
                        }

                        var videosUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/videos?api_key={apiKey}";
                        var videos = await _http.GetFromJsonAsync<TmdbVideosResponse>(videosUrl);
                        movie.TrailerKey = videos?.Results?.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer")?.Key;

                        var keywordsUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/keywords?api_key={apiKey}";
                        var keywordResponse = await _http.GetFromJsonAsync<TmdbKeywordResponse>(keywordsUrl);
                        if (keywordResponse != null)
                        {
                            movie.Keywords = keywordResponse.AllKeywords
                                .Where(k => !string.IsNullOrWhiteSpace(k.Name))
                                .GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
                                .Select(g => g.First())
                                .OrderBy(k => k.Name)
                                .ToList();
                        }

                        var ratingsUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/content_ratings?api_key={apiKey}";
                        var ratings = await _http.GetFromJsonAsync<TmdbTvContentRatingsResponse>(ratingsUrl);
                        if (ratings != null)
                        {
                            movie.Certifications = ratings.Results
                                .Select(r => new TmdbCertification
                                {
                                    Region = r.Region,
                                    Rating = r.Rating?.Trim() ?? ""
                                })
                                .Where(c => !string.IsNullOrWhiteSpace(c.Region) && !string.IsNullOrWhiteSpace(c.Rating))
                                .OrderBy(c => c.Region)
                                .ToList();
                        }
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

    public async Task<TmdbMovie?> GetTmdbDetailsByImdbIdAsync(string imdbId)
    {
        if (string.IsNullOrEmpty(imdbId) || imdbId.StartsWith("movie-") || imdbId.StartsWith("tv-")) return null;
        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/find/{imdbId}?api_key={apiKey}&external_source=imdb_id";
        try
        {
            var res = await _http.GetFromJsonAsync<TmdbFindResult>(url);
            var m = res?.MovieResults?.FirstOrDefault();
            if (m != null) return await GetTmdbDetailsByIdAsync(m.Id, "movie");
            var t = res?.TvResults?.FirstOrDefault();
            if (t != null) return await GetTmdbDetailsByIdAsync(t.Id, "tv");
        }
        catch { }
        return null;
    }

    public async Task<List<TmdbCast>> SearchTmdbPeopleAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/search/person?api_key={apiKey}&query={Uri.EscapeDataString(query)}";
        try
        {
            var res = await _http.GetFromJsonAsync<TmdbPersonSearchResponse>(url);
            return res?.Results?.Select(r => new TmdbCast { Id = r.Id, Name = r.Name, ProfilePath = r.ProfilePath }).ToList() ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<TmdbSearchResultItem>> SearchTmdbMultiAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/search/multi?api_key={apiKey}&query={Uri.EscapeDataString(query)}&include_adult=false";
        try
        {
            var res = await _http.GetFromJsonAsync<TmdbSearchResponse>(url);
            return res?.Results?.Where(r => r.MediaType == "movie" || r.MediaType == "tv").ToList() ?? new();
        }
        catch { return new(); }
    }

    public async Task<HashSet<int>> GetPersonCreditsIdsAsync(int personId)
    {
        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/person/{personId}/combined_credits?api_key={apiKey}";
        try
        {
            var res = await _http.GetFromJsonAsync<TmdbPersonCombinedCredits>(url);
            var ids = new HashSet<int>();
            if (res != null)
            {
                foreach (var c in res.Cast) ids.Add(c.Id);
                foreach (var c in res.Crew) ids.Add(c.Id);
            }
            return ids;
        }
        catch { return new(); }
    }

    public async Task<TmdbCollection?> GetTmdbCollectionAsync(int collectionId)
    {
        if (_collectionCache.TryGetValue(collectionId, out var cached)) return cached;

        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/collection/{collectionId}?api_key={apiKey}";
        try
        {
            var col = await _http.GetFromJsonAsync<TmdbCollection>(url);
            if (col != null)
            {
                _collectionCache[collectionId] = col;
                return col;
            }
        }
        catch (Exception ex) { Console.WriteLine($"Collection API error: {ex.Message}"); }
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
        _detailsStore.Clear();
        await PersistAsync();
        NotifyStateChanged();
    }

    private async Task PersistAsync()
    {
        var slim = Items.Select(i => new WatchlistItemSlim
        {
            ImdbId         = i.ImdbId,
            Title          = i.Title,
            TitleType      = i.TitleType,
            Year           = i.Year,
            Genres         = i.Genres,
            Director       = i.Director,
            PosterPath     = i.PosterPath,
            ParsedYear     = i.ParsedYear,
            Status         = i.Status,
            CurrentSeason  = i.CurrentSeason,
            CurrentEpisode = i.CurrentEpisode,
            DateAdded      = i.DateAdded,
            UserRating     = i.UserRating,
            Rating20       = i.Rating20,
            TmdbId         = i.TmdbId,
            Runtime        = i.Runtime,
            Collection     = i.Collection
        }).ToList();

        // Merge any runtime-populated detail fields back into the details store
        foreach (var item in Items)
        {
            if (!string.IsNullOrEmpty(item.Overview) || item.VoteAverage.HasValue || !string.IsNullOrEmpty(item.OriginalTitle))
            {
                _detailsStore[item.ImdbId] = new WatchlistItemDetails
                {
                    ImdbId       = item.ImdbId,
                    OriginalTitle= item.OriginalTitle,
                    Overview     = item.Overview,
                    VoteAverage  = item.VoteAverage,
                };
            }
        }

        await _storage.SaveCompressedListAsync("my_movie_list_slim", slim);
        await _storage.SaveCompressedAsync("my_movie_details", _detailsStore);
        await _storage.SaveAsync("my_custom_collections", Collections);
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
        Console.WriteLine($"AddItemAsync called: {item.Title} (IMDB: {item.ImdbId})");
        Console.WriteLine($"Current items count: {Items.Count}");
        
        if (!Items.Any(i => i.ImdbId == item.ImdbId))
        {
            Console.WriteLine("Item not found in collection, adding...");
            if (item.DateAdded == default) item.DateAdded = DateTime.Now;
            Items.Add(item);
            Console.WriteLine($"Item added. New count: {Items.Count}");
            await UpdateListAsync(Items);
            Console.WriteLine("UpdateListAsync completed");
        }
        else
        {
            Console.WriteLine("Item already exists in collection, not adding");
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

    public void ToggleWatchingSort(string column)
    {
        if (WatchingSortColumn == column)
        {
            WatchingSortDescending = !WatchingSortDescending;
        }
        else
        {
            WatchingSortColumn = column;
            WatchingSortDescending = (column == "DateAdded" || column == "Year");
        }
        NotifyStateChanged();
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
        if (_showDetailsInProgress && SelectedItem == item) return;
        _showDetailsInProgress = true;

        try
        {
            SelectedItem = item;
            SelectedMovie = new TmdbMovie 
            { 
                Title = item.Title, 
                OriginalTitle = item.OriginalTitle,
                ReleaseDate = item.Year,
                Overview = item.Overview ?? ""
            };

            IsModalOpen = false;
            IsSidePanelOpen = true; // Always open SidePanel

            IsLoadingDetails = true;
            NotifyStateChanged(fullRefresh: false);

            try { _lastIsMobile = await _js.InvokeAsync<bool>("uiHelpers.isMobile"); } catch { }

            var details = await GetTmdbDetailsAsync(item.ImdbId);
            if (details != null && SelectedItem == item)
            {
                SelectedMovie = details;
                await HydrateMissingMetadataAsync(item, details);
                IsLoadingDetails = false;
                NotifyStateChanged(fullRefresh: false);
            }
        }
        finally
        {
            _showDetailsInProgress = false;
        }
    }

    // Track hydration lifecycle per session so failed fetches can retry after a cooldown.
    private readonly HashSet<string> _hydratedThisSession = new();
    private readonly HashSet<string> _hydratingNow = new();
    private readonly Dictionary<string, DateTimeOffset> _hydrationRetryAfter = new();
    private static readonly TimeSpan HydrationRetryCooldown = TimeSpan.FromMinutes(5);

    private async Task BackgroundHydrationLoop()
    {
        await Task.Delay(5000); // Initial boot delay
        
        while (true)
        {
            int nextDelay = 5000; // Idle delay
            try
            {
                if (FetchPreference == DataFetchPreference.Background)
                {
                    var item = Items
                        .Where(i => NeedsHydration(i) && CanAttemptHydration(i.ImdbId))
                        .OrderByDescending(i => string.IsNullOrEmpty(i.Overview))
                        .FirstOrDefault();

                    if (item != null)
                    {
                        nextDelay = 600; // Active work delay
                        _hydratingNow.Add(item.ImdbId);
                        try
                        {
                            var details = await GetTmdbEssentialsAsync(item.ImdbId);
                            if (details != null)
                            {
                                if (await HydrateMissingMetadataAsync(item, details))
                                {
                                    _ = ShowToastAsync($"Synced: {item.Title}", 2500);
                                    _movieCache.Remove(item.ImdbId);
                                }
                            }

                            if (NeedsHydration(item))
                                _hydrationRetryAfter[item.ImdbId] = DateTimeOffset.UtcNow.Add(HydrationRetryCooldown);
                            else
                            {
                                _hydratedThisSession.Add(item.ImdbId);
                                _hydrationRetryAfter.Remove(item.ImdbId);
                            }
                        }
                        finally { _hydratingNow.Remove(item.ImdbId); }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sync Error: {ex.Message}");
                nextDelay = 10000; // Back off on error
            }

            await Task.Delay(nextDelay);
        }
    }

    // Lightweight fetch for background sync: only calls find + credits (no images/videos)
    private async Task<TmdbMovie?> GetTmdbEssentialsAsync(string imdbId)
    {
        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/find/{imdbId}?api_key={apiKey}&external_source=imdb_id";
        try
        {
            var response = await _http.GetFromJsonAsync<TmdbFindResult>(url);
            TmdbMovie? movie = null;

            if (response?.MovieResults?.Any() == true)
            {
                movie = response.MovieResults.First();
                var creditsUrl = $"https://api.themoviedb.org/3/movie/{movie.Id}/credits?api_key={apiKey}";
                var credits = await _http.GetFromJsonAsync<TmdbCredits>(creditsUrl);
                if (credits != null)
                    movie.Credits = credits;
            }
            else if (response?.TvResults?.Any() == true)
            {
                var tv = response.TvResults.First();
                movie = new TmdbMovie
                {
                    Id = tv.Id, Title = tv.Name, OriginalTitle = tv.OriginalName,
                    Overview = tv.Overview, PosterPath = tv.PosterPath, GenreList = tv.GenreList
                };
                var creditsUrl = $"https://api.themoviedb.org/3/tv/{tv.Id}/credits?api_key={apiKey}";
                var credits = await _http.GetFromJsonAsync<TmdbCredits>(creditsUrl);
                if (credits != null)
                    movie.Credits = credits;
            }

            return movie;
        }
        catch { return null; }
    }

    private async Task<bool> HydrateMissingMetadataAsync(WatchlistItem listItem, TmdbMovie details)
    {
        bool changed = false;
        
        if (string.IsNullOrEmpty(listItem.Overview) && !string.IsNullOrEmpty(details.Overview)) { listItem.Overview = details.Overview; changed = true; }
        if (string.IsNullOrEmpty(listItem.Genres) && details.GenreList.Any()) { listItem.Genres = string.Join(", ", details.GenreList.Select(g => g.Name)); changed = true; }
        if (string.IsNullOrEmpty(listItem.Director) && details.Credits?.Crew?.Any(c => c.Job == "Director") == true) { listItem.Director = details.Credits.Crew.First(c => c.Job == "Director").Name; changed = true; }
        if (string.IsNullOrEmpty(listItem.PosterPath) && !string.IsNullOrEmpty(details.PosterPath)) { listItem.PosterPath = details.PosterPath; changed = true; }
        if (!listItem.VoteAverage.HasValue && details.VoteAverage > 0) { listItem.VoteAverage = details.VoteAverage; changed = true; }
        
        if (changed)
        {
            await UpdateListAsync(Items);
            NotifyStateChanged(fullRefresh: false);
        }
        
        return changed;
    }

    private static bool NeedsHydration(WatchlistItem item)
        => !string.IsNullOrEmpty(item.ImdbId) && (
            string.IsNullOrEmpty(item.Overview) ||
            string.IsNullOrEmpty(item.Genres) ||
            string.IsNullOrEmpty(item.Director) ||
            string.IsNullOrEmpty(item.PosterPath));

    private bool CanAttemptHydration(string imdbId)
    {
        if (string.IsNullOrEmpty(imdbId)) return false;
        if (_hydratedThisSession.Contains(imdbId)) return false;
        if (_hydratingNow.Contains(imdbId)) return false;

        if (_hydrationRetryAfter.TryGetValue(imdbId, out var retryAfter) && retryAfter > DateTimeOffset.UtcNow)
            return false;

        return true;
    }

    public async Task ShowDetailsAsync(TmdbSearchResultItem searchItem)
    {
        if (_showDetailsInProgress) return;
        _showDetailsInProgress = true;

        try
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

            IsModalOpen = false;
            IsSidePanelOpen = true; // Always open SidePanel

            IsLoadingDetails = true;
            NotifyStateChanged(fullRefresh: false);

            try { _lastIsMobile = await _js.InvokeAsync<bool>("uiHelpers.isMobile"); } catch { }

            var details = await GetTmdbDetailsByIdAsync(searchItem.Id, searchItem.MediaType);
            if (details != null && SelectedItem == tempItem)
            {
                SelectedMovie = details;
                SelectedItem.ImdbId = details.ImdbId ?? "";
                IsLoadingDetails = false;
                NotifyStateChanged(fullRefresh: false);
            }
        }
        finally
        {
            _showDetailsInProgress = false;
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

    public async Task ShowRandomFromFilteredAsync(WatchlistStatus currentStatus)
    {
        var pool = (currentStatus == WatchlistStatus.Watched ? WatchedItems : FilteredItems).ToList();
        if (pool.Any())
        {
            var random = pool[Random.Shared.Next(pool.Count)];
            await ShowDetailsAsync(random);
        }
        else
        {
            _ = ShowToastAsync("No matches found for your current filters!");
        }
    }

    public async Task ShowGlobalRandomMovieAsync()
    {
        var apiKey = _config["TmdbApiKey"];
        try
        {
            // Pick a random page from the top 500 pages of popular movies
            int randomPage = Random.Shared.Next(1, 501);
            var url = $"https://api.themoviedb.org/3/discover/movie?api_key={apiKey}&sort_by=popularity.desc&page={randomPage}&vote_count.gte=100&include_adult=false";
            
            var response = await _http.GetFromJsonAsync<TmdbSearchResponse>(url);
            if (response?.Results != null && response.Results.Any())
            {
                // Filter out items already in the user's library
                var results = response.Results.Where(r => 
                    !Items.Any(i => i.Title.Equals(r.Title, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                if (!results.Any()) results = response.Results; // Fallback if all are in library

                var chosen = results[Random.Shared.Next(results.Count)];
                chosen.MediaType = "movie"; // discover/movie only returns movies
                await ShowDetailsAsync(chosen);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Global random error: {ex.Message}");
            _ = ShowToastAsync("Failed to fetch a random movie. Check your connection.");
        }
    }

    public void CloseModal()
    {
        IsModalOpen = false;
        IsSidePanelOpen = false; // Also close side panel if it was open
        SelectedItem = null;
        SelectedMovie = null;
        NotifyStateChanged(fullRefresh: false);
    }

    public void CloseSidePanel()
    {
        IsSidePanelOpen = false;
        SelectedItem = null;
        SelectedMovie = null;
        NotifyStateChanged(fullRefresh: false);
    }

    public async Task OpenFranchisePageAsync(TmdbCollection collection)
    {
        IsFranchisePageOpen = true;
        SelectedCollection = collection;
        NotifyStateChanged(fullRefresh: false);
    }

    public void CloseFranchisePage()
    {
        IsFranchisePageOpen = false;
        SelectedCollection = null;
        NotifyStateChanged(fullRefresh: false);
    }

    public async Task OpenPersonProfileAsync(int personId)
    {
        _nav.NavigateTo($"person/{personId}");
    }

    public void ClosePersonProfile()
    {
        IsPersonProfileOpen = false;
        SelectedPerson = null;
        SelectedPersonCredits = null;
        NotifyStateChanged(fullRefresh: false);
    }

    private async Task<TmdbPerson?> GetPersonDetailsAsync(int personId)
    {
        if (_personCache.TryGetValue(personId, out var cached)) return cached;

        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/person/{personId}?api_key={apiKey}";
        try
        {
            var person = await _http.GetFromJsonAsync<TmdbPerson>(url);
            if (person != null)
            {
                _personCache[personId] = person;
                return person;
            }
        }
        catch (Exception ex) { Console.WriteLine($"Person details API error: {ex.Message}"); }
        return null;
    }

    private async Task<TmdbPersonCombinedCredits?> GetPersonCreditsAsync(int personId)
    {
        if (_personCreditsCache.TryGetValue(personId, out var cached)) return cached;

        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/person/{personId}/combined_credits?api_key={apiKey}";
        try
        {
            var credits = await _http.GetFromJsonAsync<TmdbPersonCombinedCredits>(url);
            if (credits != null)
            {
                // Sort by popularity and filter out very obscure titles if needed
                credits.Cast = credits.Cast.OrderByDescending(c => c.VoteAverage).ToList();
                credits.Crew = credits.Crew.OrderByDescending(c => c.VoteAverage).ToList();
                _personCreditsCache[personId] = credits;
                return credits;
            }
        }
        catch (Exception ex) { Console.WriteLine($"Person credits API error: {ex.Message}"); }
        return null;
    }

    public async Task<WikipediaSnippet?> GetWikipediaSnippetAsync(string title, int? year = null)
    {
        try
        {
            string[] searchAttempts = year.HasValue 
                ? new[] { $"{title} ({year} film)", $"{title} (film)", title }
                : new[] { $"{title} (film)", title };

            foreach (var query in searchAttempts)
            {
                var escapedQuery = Uri.EscapeDataString(query);
                // First: Find the correct title and check for disambiguation
                var url = $"https://en.wikipedia.org/w/api.php?action=query&prop=extracts&exintro=1&explaintext=1&titles={escapedQuery}&format=json&origin=*&formatversion=2";
                var result = await _http.GetFromJsonAsync<WikipediaApiResponse>(url);
                
                var page = result?.Query?.Pages?.FirstOrDefault();
                if (page != null && !page.Missing && !string.IsNullOrWhiteSpace(page.Extract))
                {
                    var isDisambiguation = page.Extract.Contains("most commonly refers to:", StringComparison.OrdinalIgnoreCase) || 
                                          page.Extract.Contains("may refer to:", StringComparison.OrdinalIgnoreCase) ||
                                          (page.Title?.Contains("(disambiguation)", StringComparison.OrdinalIgnoreCase) ?? false);
                    
                    if (!isDisambiguation)
                    {
                        // Second: Get actual HTML content with links using action=parse
                        var actualTitle = page.Title ?? query;
                        var parseUrl = $"https://en.wikipedia.org/w/api.php?action=parse&page={Uri.EscapeDataString(actualTitle)}&prop=text&format=json&origin=*";
                        var parseResult = await _http.GetFromJsonAsync<WikipediaParseResponse>(parseUrl);
                        var html = parseResult?.Parse?.Text?.Value;

                        if (!string.IsNullOrWhiteSpace(html))
                        {
                            return new WikipediaSnippet { 
                                Title = actualTitle, 
                                Extract = html,
                                ContentUrls = new WikipediaUrls { 
                                    Desktop = new WikipediaDesktopUrls { 
                                        Page = $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(actualTitle)}" 
                                    } 
                                }
                            };
                        }
                    }
                }
            }
            return null;
        }
        catch { return null; }
    }

    public async Task<List<OpenSubtitlesData>> GetSubtitlesAsync(string imdbId)
    {
        if (string.IsNullOrEmpty(OpenSubtitlesApiKey)) return new();

        try
        {
            var id = imdbId.StartsWith("tt") ? imdbId[2..] : imdbId;
            var url = $"https://api.opensubtitles.com/api/v1/subtitles?imdb_id={id}";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Api-Key", OpenSubtitlesApiKey);
            request.Headers.Add("User-Agent", "MovieLogApp_v1.0");

            var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OpenSubtitlesSearchResult>();
                return result?.Data ?? new();
            }
        }
        catch (Exception ex) { Console.WriteLine($"Search subtitles error: {ex.Message}"); }
        return new();
    }
    public async Task CreateCollectionAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Collections.Add(new CustomCollection { Name = name });
        await PersistAsync();
        NotifyStateChanged();
    }

    public async Task DeleteCollectionAsync(Guid id)
    {
        var col = Collections.FirstOrDefault(c => c.Id == id);
        if (col != null)
        {
            Collections.Remove(col);
            await PersistAsync();
            NotifyStateChanged();
        }
    }

    public async Task RenameCollectionAsync(Guid id, string newName)
    {
        var col = Collections.FirstOrDefault(c => c.Id == id);
        if (col != null && !string.IsNullOrWhiteSpace(newName))
        {
            col.Name = newName;
            await PersistAsync();
            NotifyStateChanged();
        }
    }

    public async Task AddMovieToCollectionAsync(Guid collectionId, string imdbId)
    {
        var col = Collections.FirstOrDefault(c => c.Id == collectionId);
        if (col != null && !col.Items.Any(i => i.ImdbId == imdbId))
        {
            var maxOrder = col.Items.Any() ? col.Items.Max(i => i.Order) : -1;
            col.Items.Add(new CollectionItem { ImdbId = imdbId, Order = maxOrder + 1 });
            
            // Set poster if not set
            if (string.IsNullOrEmpty(col.PosterPath))
            {
                var item = Items.FirstOrDefault(i => i.ImdbId == imdbId);
                if (item != null) col.PosterPath = item.PosterPath;
            }

            await PersistAsync();
            NotifyStateChanged();
        }
    }

    public async Task BulkAddMoviesToCollectionAsync(Guid collectionId, IEnumerable<string> imdbIds)
    {
        var col = Collections.FirstOrDefault(c => c.Id == collectionId);
        if (col != null)
        {
            bool added = false;
            var maxOrder = col.Items.Any() ? col.Items.Max(i => i.Order) : -1;
            
            foreach (var id in imdbIds)
            {
                if (!col.Items.Any(i => i.ImdbId == id))
                {
                    col.Items.Add(new CollectionItem { ImdbId = id, Order = ++maxOrder });
                    added = true;
                }
            }

            if (added)
            {
                if (string.IsNullOrEmpty(col.PosterPath) && col.Items.Any())
                {
                    var firstId = col.Items.OrderBy(i => i.Order).First().ImdbId;
                    var item = Items.FirstOrDefault(i => i.ImdbId == firstId);
                    if (item != null) col.PosterPath = item.PosterPath;
                }
                await PersistAsync();
                NotifyStateChanged();
            }
        }
    }

    public async Task RemoveMovieFromCollectionAsync(Guid collectionId, string imdbId)
    {
        var col = Collections.FirstOrDefault(c => c.Id == collectionId);
        if (col != null)
        {
            var itemToRemove = col.Items.FirstOrDefault(i => i.ImdbId == imdbId);
            if (itemToRemove != null)
            {
                col.Items.Remove(itemToRemove);
                
                // Update poster if it was the one removed
                var item = Items.FirstOrDefault(i => i.ImdbId == imdbId);
                if (item != null && col.PosterPath == item.PosterPath)
                {
                    if (col.Items.Any())
                    {
                        var nextId = col.Items.OrderBy(i => i.Order).First().ImdbId;
                        var nextItem = Items.FirstOrDefault(i => i.ImdbId == nextId);
                        col.PosterPath = nextItem?.PosterPath;
                    }
                    else
                    {
                        col.PosterPath = null;
                    }
                }

                await PersistAsync();
                NotifyStateChanged();
            }
        }
    }

    public async Task MoveCollectionItemUpAsync(Guid collectionId, string imdbId)
    {
        var col = Collections.FirstOrDefault(c => c.Id == collectionId);
        if (col == null) return;

        var sorted = col.Items.OrderBy(i => i.Order).ToList();
        var idx = sorted.FindIndex(i => i.ImdbId == imdbId);
        if (idx <= 0) return; // Already at top or not found

        // Swap order values with preceding item
        var current = sorted[idx];
        var prev = sorted[idx - 1];
        
        int temp = current.Order;
        current.Order = prev.Order;
        prev.Order = temp;

        await PersistAsync();
        NotifyStateChanged();
    }

    public async Task MoveCollectionItemDownAsync(Guid collectionId, string imdbId)
    {
        var col = Collections.FirstOrDefault(c => c.Id == collectionId);
        if (col == null) return;

        var sorted = col.Items.OrderBy(i => i.Order).ToList();
        var idx = sorted.FindIndex(i => i.ImdbId == imdbId);
        if (idx < 0 || idx >= sorted.Count - 1) return; // Already at bottom or not found

        // Swap order values with following item
        var current = sorted[idx];
        var next = sorted[idx + 1];
        
        int temp = current.Order;
        current.Order = next.Order;
        next.Order = temp;

        await PersistAsync();
        NotifyStateChanged();
    }

    public async Task SetCollectionItemRankAsync(Guid collectionId, string imdbId, int newRank)
    {
        var col = Collections.FirstOrDefault(c => c.Id == collectionId);
        if (col == null) return;

        var items = col.Items.OrderBy(i => i.Order).ToList();
        var itemToMove = items.FirstOrDefault(i => i.ImdbId == imdbId);
        if (itemToMove == null) return;

        int oldIdx = items.IndexOf(itemToMove);
        int newIdx = Math.Clamp(newRank - 1, 0, items.Count - 1);

        if (oldIdx == newIdx) return;

        items.RemoveAt(oldIdx);
        items.Insert(newIdx, itemToMove);

        // Re-assign orders
        for (int i = 0; i < items.Count; i++)
        {
            items[i].Order = i;
        }

        await PersistAsync();
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

public class WikipediaParseResponse
{
    [JsonPropertyName("parse")]
    public WikipediaParse? Parse { get; set; }
}

public class WikipediaParse
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    [JsonPropertyName("text")]
    public WikipediaText? Text { get; set; }
}

public class WikipediaText
{
    [JsonPropertyName("*")]
    public string? Value { get; set; }
}
