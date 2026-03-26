import re

with open(r"e:\Works\movie-log\WatchlistService.cs", "r", encoding="utf-8") as f:
    text = f.read()

target = r"""            if \(response\?\.MovieResults\?\.Any\(\) == true\)\s*
            \{.*?
            if \(response\?\.TvResults\?\.Any\(\) == true\)\s*
            \{.*?
                _movieCache\[imdbId\] = movie;\s*
                return movie;\s*
            \}"""

replacement = """            if (response?.MovieResults?.Any() == true) 
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
            }"""

new_text, count = re.subn(target, replacement, text, flags=re.DOTALL)

if count > 0:
    with open(r"e:\Works\movie-log\WatchlistService.cs", "w", encoding="utf-8") as f:
        f.write(new_text)
    print("Replaced successfully")
else:
    print("Not found")
