# Movie Log

Try it online: [ahmetbgr.github.io/movie-log/](https://ahmetbgr.github.io/movie-log/)

Movie Log is a personal movie and TV show tracking application developed with Blazor WebAssembly. It works entirely within the browser and stores all data locally on your machine. Various AIs heavily used in development because I know nothing about web development. 

<p align="center">
  <img src="https://github.com/user-attachments/assets/39ef61ee-c5aa-44bf-920d-e5f2d32edc9c" width="300" />
  <img src="https://github.com/user-attachments/assets/49adac64-52f4-4493-b072-2f4271b91ade" width="300" />
  <img src="https://github.com/user-attachments/assets/2cdaca49-f76b-4891-a3ac-c70336f7ef18" width="300" />
  <img src="https://github.com/user-attachments/assets/538069a0-7e3c-494d-bf20-0ce6b9ea7279" width="300" />
  <img src="https://github.com/user-attachments/assets/9d3475d3-6a5e-463c-8931-bb05b9e52965" width="300" />
  <img src="https://github.com/user-attachments/assets/e8597b07-794f-4245-95ee-75f3937efe93" width="300" />
  <img src="https://github.com/user-attachments/assets/95c2fc10-7daf-47c3-a455-42f76d51ff13" width="300" />
  <img src="https://github.com/user-attachments/assets/7d4e2e9f-af85-4fc6-bd0c-5759dc38d850" width="100" />
</p>

## Features

- **Library Management**: Organize titles into Watchlist, Currently Watching, and History categories. Create custom collections.
- **TV Progress Tracking**: Log current season and episode numbers for television series.
- **Scoring System**: Rate watched titles on a scale of 0 to 100 with dynamic color feedback.
- **Metadata Integration**: Automatically retrieve plots, posters, and cast information from TMDB.
- **Background Hydration**: Optional background service to complete missing library details while idle.
- **Data Portability**: Import existing collections from Critcker, IMDb, Letterboxd, or Trakt CSV exports.
- **Global Search**: Search the TMDB database directly from the application with local search history.
- **Privacy Focus**: All movie data, ratings, and search history are stored exclusively in your browser's local storage.
- **Open On**: Quickly open movies on the website of your own choice.
- **Discovery**: See new releases, popular movies or upcoming titles.
- **Library Sync**: Optional library sync between your devices with Github Gists.
- **Statistics**: See statistics about your library in home page.

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
