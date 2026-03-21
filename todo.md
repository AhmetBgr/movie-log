# Project Roadmap & Todo List

## Completed (
- [x] **Togglable Filter Bars**: Save space in Watchlist and Home views.
- [x] **Dynamic Table Expansion**: Table grows to fill the screen when filters are hidden.
- [x] **Instant UI**: Removed all fade animations for immediate response.
- [x] **Instant Modals**: Display basic movie info immediately while full details load in the background.
- [x] **Genre Filtering**: Added a global genre dropdown to the Watchlist table.
- [x] **Stabilized Virtualization**: Fixed "fast scrolling" and jumpy table behavior.
- [x] **Global Search**: Search TMDB from any page with consistent results styling.
- [x] **Wider Scrollbars**: Doubled the scrollbar width (16px) for easier navigation.
- [x] **Watching Tab**: Dedicated space for tracking active movies and TV shows.
- [x] **Episode Tracking**: Increment/decrement buttons for Season and Episode.
- [x] **Watched History**: Dedicated "Watched" tab with personalized ratings (1-10).
- [x] **Chronological Tracking**: "Date Added" timestamps for every item across all lists.
- [x] **History Import**: "Import as Watched" toggle in Setup to skip the watchlist.
- [x] **High-Fidelity Lightbox**: Full-screen image preview with blurred backdrop and instant dismissal.
- [x] **Persistent Movie Plots**: Instant rendering from local storage and silent background sync.
- [x] **Global Row Striping**: Improved table readability with high-contrast row-based differentiation.
- [x] **Full-Plot Table Displays**: Unshortened summaries directamente in the Watchlist grid.
- [x] **Stabilized Modal Layout**: Fixed-height media column to prevent UI jumping during view toggles.


## TODO
- [ ] **Smart Collections (Franchises)**: franchise tag leading to franchise page in movie details.
- [ ] **Cast & Crew Deep-Dive**: Clickable actors/directors to show profiles, bios, and their other work.
- [ ] **Auto-Import Bookmarklet**: A browser tool to instantly add movies from IMDb/Letterboxd to your local Log.
- [ ] **Export to CSV**: Export your filtered Watchlist view back to a file.
- [ ] **Watchlist Statistics**: Pie charts or stats for genre distribution and year ranges.
- [ ] **Cloud Backup**: Optional integration for saving your list beyond local storage.
- [ ] **Dark/Light Mode Toggle**: Manual theme switcher (currently defaults to system/premium dark).
- [ ] **open on**: button next to tiltes which will redirect to selected site(imdb, 1337x search, r/movies, criticker, letterboxd)
- [ ] **global random**: show random movie outside of library.
- [ ] **GitHub Gist library sync**


## Known Issues & small improvements
- [ ] Continuous performance monitoring for 1000+ item lists(??).
- [ ] Add unit tests for `WatchlistService` filtering logic(??).
- [ ] search page should sort by relevance by default.
- [ ] fix saved box showing at wrong place on watched page when editing rating
- [ ] fix tracking controls on tv shows not appearing on mobile
- [ ] show posters on watched and watching pages. improve tables and show card view option on these pages
- [ ] make sorting buttons appear over table so that we can change sorting when even scrolled down.
- [ ] reduce Corner softening on buttons
- [ ] fix filter box rendering over search and side panel
- [ ] remove movii log label on side panel
- [ ] fix auto focux element on first load
- [ ] try to improve load time
- [ ] consider removing pwa
- [ ] x button next to search text to quick deletion
- [ ] fix site not loading on mobile firefox
