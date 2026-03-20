# Movie Log 🎬

A personal movie watchlist management application built with Blazor WebAssembly.

## Features

- 📝 **Watchlist Management**: Add, remove, and track movies you want to watch
- 🎯 **Movie Details**: Store comprehensive movie information (title, year, genres, director, rating)
- 🌐 **IMDb Integration**: Browser extension to add movies directly from IMDb
- 💾 **Local Storage**: All data stored locally in your browser
- 📱 **Responsive Design**: Works on desktop and mobile devices
- 🔄 **Real-time Updates**: Instant synchronization between IMDb and your watchlist

## Live Demo

🌐 **[View Live Application](https://ahmetbgr.github.io/movie-log/)**

## IMDb Browser Extension

The browser extension allows you to add movies to your watchlist directly from IMDb pages:

### Installation
1. Download the extension from the `browser-extension/` folder
2. Open Firefox and go to `about:debugging`
3. Click "Load Temporary Add-on" and select `browser-extension/manifest.json`

### Features
- ✅ Add movies from IMDb with one click
- ✅ Remove movies from watchlist
- ✅ See if movie is already in your watchlist
- ✅ Cross-domain communication with your Movie Log app
- ✅ Real-time updates without page refresh

## Getting Started Locally

### Prerequisites
- .NET 10.0 SDK
- Visual Studio 2022 or VS Code

### Running the Application
1. Clone the repository:
   ```bash
   git clone https://github.com/AhmetBgr/movie-log.git
   cd movie-log
   ```

2. Run the application:
   ```bash
   dotnet run
   ```

3. Open your browser and navigate to `https://localhost:7008`

## Project Structure

```
movie-log/
├── 📁 browser-extension/          # Firefox browser extension
│   ├── manifest.json             # Extension configuration
│   ├── background.js             # Cross-domain communication
│   ├── content/                  # Content scripts
│   └── popup/                    # Extension popup UI
├── 📁 Components/                # Reusable Blazor components
├── 📁 Pages/                     # Blazor pages
├── 📁 Services/                  # Application services
├── 📁 wwwroot/                   # Static assets
├── 📄 Program.cs                 # Application entry point
├── 📄 WatchlistService.cs        # Watchlist management logic
└── 📄 Models.cs                  # Data models
```

## Technology Stack

- **Frontend**: Blazor WebAssembly
- **Language**: C# 10.0
- **Styling**: CSS3
- **Storage**: Browser LocalStorage
- **Extension**: Firefox WebExtensions API
- **Deployment**: GitHub Pages

## Data Storage

The application uses browser localStorage to store your watchlist data. All data is stored locally in your browser under the key `my_movie_list`.

### Data Format
```json
{
  "ImdbId": "tt1234567",
  "Title": "Movie Title",
  "TitleType": "Movie",
  "Year": "2024",
  "Genres": "Action, Adventure",
  "Director": "Director Name",
  "ParsedYear": 2024,
  "Status": 0,
  "DateAdded": "2024-03-20",
  "UserRating": null,
  "Rating20": null,
  "Overview": null
}
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### Development Setup
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test the application
5. Submit a Pull Request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- IMDb for movie data
- Blazor team for the amazing framework
- Firefox for extension support

---

**Made with ❤️ for movie lovers** 🍿
=======

>>>>>>> e2b929a3d19396ab1f1389ee9c1bfa73d5373d7a
