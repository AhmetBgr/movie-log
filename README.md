# Movie Log

Try it online: [ahmetbgr.github.io/movie-log/](https://ahmetbgr.github.io/movie-log/)

Movie Log is a personal movie and TV show tracking application developed with Blazor WebAssembly. It works entirely within the browser and stores all data locally on your machine. Various AIs heavily used in development because I know nothing about web development. 

## Features

- **Collection Management**: Organize titles into Watchlist, Currently Watching, and History categories.
- **TV Progress Tracking**: Log current season and episode numbers for television series.
- **Scoring System**: Rate watched titles on a scale of 0 to 100 with dynamic color feedback.
- **Metadata Integration**: Automatically retrieve plots, posters, and cast information from TMDB.
- **Background Hydration**: Optional background service to complete missing library details while idle.
- **Data Portability**: Import existing collections from Critcker, IMDb, Letterboxd, or Trakt CSV exports.
- **Global Search**: Search the TMDB database directly from the application with local search history.
- **Privacy Focus**: All movie data, ratings, and search history are stored exclusively in your browser's local storage.

## Getting Started

### Prerequisites

- .NET 8.0 SDK or newer
- A modern web browser

### Running Locally

1. Clone the repository:
   ```bash
   git clone https://github.com/AhmetBgr/movie-log.git
   cd movie-log
   ```

2. Start the application:
   ```bash
   dotnet run
   ```

3. Navigate to the local URL provided in the terminal (typically `https://localhost:7008`).

## Technical Profile

- **Framework**: Blazor WebAssembly
- **Language**: C#
- **Styling**: Vanilla CSS / Bootstrap 5
- **Data Persistence**: Browser LocalStorage API
- **API Provider**: The Movie Database (TMDB)

## Data Format

The application uses a flat JSON structure for items in the collection. A typical entry contains metadata such as the IMDB ID, title, original title, production year, genres, director, overview, and your personal rating or progress.

## License

This project is licensed under the MIT License.
