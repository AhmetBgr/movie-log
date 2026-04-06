# Project Roadmap & Todo List

## TODO
- [ ] **Export to CSV**: Export your filtered Watchlist view back to a file.
- [ ] **Watchlist Statistics**: Pie charts or stats for genre distribution and year ranges.
- [ ] **Dark/Light Mode Toggle**: Manual theme switcher (currently defaults to system/premium dark).
- [ ] private notes/reviews
- [ ] advanced search. similar to imdb


## Known Issues & small improvements
- [ ] reduce Corner softening on button
- [ ] fix site not loading on mobile firefox
- [ ] advanced filters
      (
        Right now, if our local library file (items.json) doesn't have the explicit string "Christopher Nolan" saved in the Director field for a specific movie, our local search box will completely miss it. Fetching full cast/            crew data for 1,000+ movies just to make the filter work would bloat the app and destroy performance.
        Since we are just brainstorming, here are the most powerful ways we could evolve the Filter Panel into a truly "Advanced" system without ruining performance:
        1. The "TMDB Intersection" Strategy (Fixes Director/Actor Search)
        Instead of trying to force our local database to learn every director, we make TMDB do the heavy lifting using an "Intersection" approach:
        Autocomplete Input: We add a "Person" search box. When you type "Denis Vil...", it pings TMDB and auto-completes to "Denis Villeneuve (ID: 137427)".
        Fetch Their Work: Once you select him, our backend explicitly asks TMDB: "Give me all TMDB IDs for movies directed by Denis Villeneuve."
        The Intersection: We take that list of TMDB IDs and just cross-reference it against your local Watched list. Why this is amazing: We instantly get perfect Director, Actor, Writer, and even Studio ("A24" or "HBO")                  filtering without having to save any of that metadata locally.
        2. Multi-Select "Pill" Tags (AND / OR Logic)
        Currently, dropdowns only let you pick one thing at a time (e.g., "Action"). An advanced panel would use multi-select pills.
        The Upgrade: You could click [Action], [Sci-Fi], and [Thriller].
        The Logic Toggle: You could hit a switch to change between OR (show movies that have any of these) and AND (show movies that have all three of these).
        3. Dual-Slider Controls for Ranges
        Right now, you are using two separate numerical inputs for "Year From / Year To".
        The Upgrade: A beautiful dual-handle range slider component for exactly filtering Release Year (1980 [---o-------o--] 2010) and Runtime (90 mins [----o----o----] 150 mins).
        4. Smart "Quick Toggle" Presets
        Sometimes you don't want to dig into dropdowns; you just want quick answers about your library. We could add a row of toggleable chips at the top of the panel:
        [Unrated by me] (Movies you watched but forgot to give a 1-100 score).
        [Added this month]
        [Short Films] (Under 90 minutes)
        [Mini-Series] (Only TV shows with 1 season)
        5. "Exclude" Filters (Negative Searching)
        Advanced users often want to filter out things rather than find them.
        The Upgrade: Clicking a Genre pill twice turns it red, meaning "Exclude". You could easily search for: "Show me all High-Rated Movies from the 1990s, EXCEPT Horror or Romance."
        The Architecture Takeaway: If we ever decide to build this, the biggest architectural shift would be moving away from "simple string matching" in WatchlistService.cs and moving toward a "Query Builder". The Filter Panel           would generate a complex query object (containing Arrays of Genres, TMDB Person IDs, Excluded Tags, etc.), and the service would execute that query by overlapping TMDB data with your local library!
      )
      
      
- [ ] episodes view
- [ ] palette?? colors=E36E40-161B4B-D8C5C5-FFFFFF-000000
