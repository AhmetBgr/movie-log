using MyPrivateWatchlist.Models;
using System.Net.Http.Json;

namespace MyPrivateWatchlist.Services;

public class WatchlistService
{
    private readonly HttpClient _http;
    private readonly LocalStorageService _storage;
    private readonly IConfiguration _config;

    public List<WatchlistItem> Items { get; private set; } = new();

    // NEW: Properties for filters that Home.razor binds to
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
        if (saved != null) Items = saved;
    }

    public async Task UpdateListAsync(List<WatchlistItem> newList)
    {
        Items = newList;
        await _storage.SaveListAsync("my_movie_list", Items);
    }

    // NEW: The "Brain" for the Random Button
    public IEnumerable<WatchlistItem> FilteredItems
    {
        get
        {
            return Items.Where(m =>
            {
                // 1. Filter by Type
                bool typeMatch = SelectedType == "All" ||
                                 m.TitleType.Equals(SelectedType, StringComparison.OrdinalIgnoreCase);

                // 2. Filter by Genre
                bool genreMatch = SelectedGenre == "All" ||
                                  m.Genres.Contains(SelectedGenre, StringComparison.OrdinalIgnoreCase);

                // 3. Filter by Year
                int sYear = 0, eYear = 0;
                bool hasStart = int.TryParse(StartYear, out sYear);
                bool hasEnd = int.TryParse(EndYear, out eYear);

                // Extract digits only from the Year string (e.g., "2024–" -> 2024)
                var yearDigits = new string(m.Year.TakeWhile(char.IsDigit).ToArray());
                int.TryParse(yearDigits, out int itemYear);

                bool yearMatch = (!hasStart || itemYear >= sYear) &&
                                 (!hasEnd || itemYear <= eYear);

                return typeMatch && genreMatch && yearMatch;
            });
        }
    }

    public async Task<TmdbMovie?> GetTmdbDetailsAsync(string imdbId)
    {
        var apiKey = _config["TmdbApiKey"];
        var url = $"https://api.themoviedb.org/3/find/{imdbId}?api_key={apiKey}&external_source=imdb_id";

        try
        {
            var response = await _http.GetFromJsonAsync<TmdbFindResult>(url);
            if (response?.MovieResults?.Any() == true) return response.MovieResults.First();
            if (response?.TvResults?.Any() == true)
            {
                var tv = response.TvResults.First();
                return new TmdbMovie
                {
                    Title = tv.Name,
                    Overview = tv.Overview,
                    PosterPath = tv.PosterPath,
                    VoteAverage = tv.VoteAverage,
                    ReleaseDate = tv.FirstAirDate
                };
            }
        }
        catch (Exception ex)
        { 
            Console.WriteLine($"API Error fetching {imdbId}: {ex.Message}");
        }
        return null;
    }
}