using System.Net;
using System.Text.RegularExpressions;
using MyPrivateWatchlist.Models;

namespace MyPrivateWatchlist.Services;

public class OpenOnLinkService
{
    private const string StorageKey = "open_on_links";

    private readonly LocalStorageService _storage;
    private Task? _loadTask;
    private List<OpenOnLink> _links = new();

    public event Action? OnChanged;

    public IReadOnlyList<OpenOnLink> Links => _links;

    public OpenOnLinkService(LocalStorageService storage)
    {
        _storage = storage;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loadTask != null)
        {
            await _loadTask;
            return;
        }

        _loadTask = LoadCoreAsync();
        await _loadTask;
    }

    public async Task AddFromSampleAsync(string? name, string? sampleUrl)
    {
        await EnsureLoadedAsync();

        var link = CreateFromSample(name, sampleUrl);
        if (_links.Any(existing => string.Equals(existing.Template, link.Template, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This template already exists.");

        _links.Add(link);
        await PersistAsync();
    }

    public async Task RemoveAsync(string id)
    {
        await EnsureLoadedAsync();

        var removed = _links.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal)) > 0;
        if (!removed)
            return;

        await PersistAsync();
    }

    public string? BuildUrl(OpenOnLink link, WatchlistItem item)
    {
        if (link == null || item == null || string.IsNullOrWhiteSpace(link.Template))
            return null;

        if (link.Template.Contains("{imdbId}", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(item.ImdbId))
            return null;

        var title = item.Title?.Trim() ?? "";
        var year = item.Year?.Trim() ?? "";
        
        var isYearOnly = year.Length == 4 && int.TryParse(year, out _);
        var appendedYear = isYearOnly ? year : "";

        var query = string.IsNullOrWhiteSpace(appendedYear) ? title : $"{title} {appendedYear}".Trim();
        var titleSlug = ToSlug(title, link.SlugCaseMode);
        var titleSlugYear = string.IsNullOrWhiteSpace(appendedYear) ? titleSlug : $"{titleSlug}-{ToSlug(appendedYear)}".Trim('-');

        return link.Template
            .Replace("{query}", WebUtility.UrlEncode(query), StringComparison.Ordinal)
            .Replace("{title}", WebUtility.UrlEncode(title), StringComparison.Ordinal)
            .Replace("{year}", WebUtility.UrlEncode(appendedYear), StringComparison.Ordinal)
            .Replace("{titleSlug}", titleSlug, StringComparison.Ordinal)
            .Replace("{titleSlugYear}", titleSlugYear, StringComparison.Ordinal)
            .Replace("{imdbId}", WebUtility.UrlEncode(item.ImdbId ?? ""), StringComparison.Ordinal);
    }

    public bool CanOpen(OpenOnLink link, WatchlistItem item)
        => !string.IsNullOrWhiteSpace(BuildUrl(link, item));

    public string? TryBuildTemplatePreview(string? sampleUrl, out string? error)
    {
        try
        {
            error = null;
            return BuildTemplate(sampleUrl, out _);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private async Task LoadCoreAsync()
    {
        _links = await _storage.GetAsync<List<OpenOnLink>>(StorageKey) ?? new List<OpenOnLink>();
        var changed = MergeDefaultLinks(_links);
        if (changed)
            await _storage.SaveAsync(StorageKey, _links);
    }

    private async Task PersistAsync()
    {
        await _storage.SaveAsync(StorageKey, _links);
        OnChanged?.Invoke();
    }

    private static OpenOnLink CreateFromSample(string? name, string? sampleUrl)
    {
        var trimmedUrl = sampleUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmedUrl))
            throw new InvalidOperationException("Paste a sample URL first.");

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Paste a valid http or https URL.");
        }

        var template = BuildTemplate(trimmedUrl, out var slugCaseMode);
        var displayName = string.IsNullOrWhiteSpace(name)
            ? InferName(uri)
            : name.Trim();

        return new OpenOnLink
        {
            Name = displayName,
            Template = template,
            SampleUrl = trimmedUrl,
            Host = uri.Host,
            SlugCaseMode = slugCaseMode,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string BuildTemplate(string? sampleUrl, out string slugCaseMode)
    {
        slugCaseMode = "title";
        var value = sampleUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Paste a sample URL first.");

        var imdbMatch = Regex.Match(value, @"tt\d{7,8}", RegexOptions.IgnoreCase);
        if (imdbMatch.Success)
            return value.Replace(imdbMatch.Value, "{imdbId}", StringComparison.OrdinalIgnoreCase);

        var queryPattern = new Regex(@"([?&](?:q|query|search|term|keyword|keywords|s)=)([^&#]+)", RegexOptions.IgnoreCase);
        if (queryPattern.IsMatch(value))
            return queryPattern.Replace(value, "$1{query}", 1);

        var searchPathPattern = new Regex(@"(/(?:search|find)/)([^/?#]+)", RegexOptions.IgnoreCase);
        if (searchPathPattern.IsMatch(value))
            return searchPathPattern.Replace(value, "$1{query}", 1);

        var slugPathPattern = new Regex(@"(/(?:film|tv|movie|show|title)/)([^/?#]+)(/?)", RegexOptions.IgnoreCase);
        var slugMatch = slugPathPattern.Match(value);
        if (slugMatch.Success)
        {
            var slug = slugMatch.Groups[2].Value;
            slugCaseMode = DetectSlugCaseMode(slug);
            var token = Regex.IsMatch(slug, @"-\d{4}$") ? "{titleSlugYear}" : "{titleSlug}";
            var replacement = $"{slugMatch.Groups[1].Value}{token}{slugMatch.Groups[3].Value}";
            return slugPathPattern.Replace(value, replacement, 1);
        }

        throw new InvalidOperationException("Could not infer a reusable template from this URL. Use a search URL or a URL containing an IMDb id.");
    }

    private static string InferName(Uri uri)
    {
        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        var name = host.Split('.').FirstOrDefault() ?? "Link";
        if (string.IsNullOrWhiteSpace(name))
            return "Link";

        return name.ToLowerInvariant() switch
        {
            "imdb" => "IMDb",
            "reddit" => "Reddit",
            "google" => "Google",
            "letterboxd" => "Letterboxd",
            "criticker" => "Criticker",
            _ => char.ToUpperInvariant(name[0]) + name[1..]
        };
    }

    private static bool MergeDefaultLinks(List<OpenOnLink> links)
    {
        var changed = false;

        var existingGoogle = links.FirstOrDefault(x => string.Equals(x.Name, "Google Search", StringComparison.OrdinalIgnoreCase));
        if (existingGoogle != null && existingGoogle.Template.Contains("sclient=gws-wiz-serp", StringComparison.OrdinalIgnoreCase))
        {
            existingGoogle.Template = "https://www.google.com/search?q={query}";
            existingGoogle.SampleUrl = "https://www.google.com/search?q=Proven+Innocent+2019";
            changed = true;
        }

        foreach (var defaults in GetDefaultLinks())
        {
            if (links.Any(existing =>
                    string.Equals(existing.Name, defaults.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existing.Template, defaults.Template, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            links.Add(defaults);
            changed = true;
        }

        return changed;
    }

    private static IEnumerable<OpenOnLink> GetDefaultLinks()
    {
        return
        [
            CreateDefaultLink("Details", "https://ahmetbgr.github.io/movie-log/movie/{imdbId}", "https://ahmetbgr.github.io/movie-log/movie/tt39150234", "ahmetbgr.github.io"),
            CreateDefaultLink("1337x Search", "https://1337x.to/search/{query}/1/", "https://1337x.to/search/dune+2022/1/", "1337x.to"),
            CreateDefaultLink("Criticker", "https://www.criticker.com/film/{titleSlug}/", "https://www.criticker.com/film/Dune/", "www.criticker.com", "title"),
            CreateDefaultLink("Google Search", "https://www.google.com/search?q={query}", "https://www.google.com/search?q=Proven+Innocent+2019", "www.google.com"),
            CreateDefaultLink("Imdb", "https://www.imdb.com/title/{imdbId}/?ref_=wl_t_1", "https://www.imdb.com/title/{imdbId}/?ref_=wl_t_1", "www.imdb.com"),
            CreateDefaultLink("Letterboxd", "https://letterboxd.com/film/{titleSlug}/", "https://letterboxd.com/film/dune/", "letterboxd.com", "lower"),
            CreateDefaultLink("r/movies", "https://www.reddit.com/r/movies/search/?q={query}&cId=b5efd88a-d45a-439d-b1b9-c2041982b614&iId=687a60fb-ae0c-40ed-8f40-bc7aea073c83", "https://www.reddit.com/r/movies/search/?q=dune%202021&cId=b5efd88a-d45a-439d-b1b9-c2041982b614&iId=687a60fb-ae0c-40ed-8f40-bc7aea073c83", "www.reddit.com"),
            CreateDefaultLink("r/truefilm", "https://www.reddit.com/r/TrueFilm/search/?q={query}&cId=9fca0a7a-7bc2-4e82-8003-9fcb8736cde0&iId=8ed540c1-28f8-43aa-8553-ba8434eb84fd", "https://www.reddit.com/r/TrueFilm/search/?q=dune%202021&cId=9fca0a7a-7bc2-4e82-8003-9fcb8736cde0&iId=8ed540c1-28f8-43aa-8553-ba8434eb84fd", "www.reddit.com")
        ];
    }

    private static OpenOnLink CreateDefaultLink(string name, string template, string sampleUrl, string host, string slugCaseMode = "title")
    {
        return new OpenOnLink
        {
            Name = name,
            Template = template,
            SampleUrl = sampleUrl,
            Host = host,
            SlugCaseMode = slugCaseMode,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string ToSlug(string? value)
        => ToSlug(value, "title");

    private static string ToSlug(string? value, string? caseMode)
    {
        var normalized = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        var cleaned = Regex.Replace(normalized, @"[^\p{L}\p{Nd}]+", "-");
        cleaned = Regex.Replace(cleaned, "-{2,}", "-").Trim('-');
        return ApplySlugCase(cleaned, caseMode);
    }

    private static string DetectSlugCaseMode(string? slug)
    {
        var value = slug ?? "";
        if (string.IsNullOrWhiteSpace(value))
            return "title";

        var letters = value.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
            return "title";

        if (letters.All(char.IsUpper))
            return "upper";

        if (letters.All(char.IsLower))
            return "lower";

        var words = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var titleStyle = words
            .Where(word => word.Any(char.IsLetter))
            .All(word =>
            {
                var firstLetterIndex = word.ToList().FindIndex(char.IsLetter);
                if (firstLetterIndex < 0)
                    return true;

                var first = word[firstLetterIndex];
                var rest = word[(firstLetterIndex + 1)..].Where(char.IsLetter);
                return char.IsUpper(first) && rest.All(char.IsLower);
            });

        return titleStyle ? "title" : "preserve";
    }

    private static string ApplySlugCase(string value, string? caseMode)
    {
        return (caseMode ?? "title").ToLowerInvariant() switch
        {
            "lower" => value.ToLowerInvariant(),
            "upper" => value.ToUpperInvariant(),
            "title" => string.Join("-", value
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Length == 0
                    ? word
                    : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant())),
            _ => value
        };
    }
}
