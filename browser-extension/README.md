# Movie Log IMDb Browser Extension

A browser extension that adds an "Add to Watchlist" button to IMDb movie pages, allowing you to quickly add movies to your personal Movie Log watchlist.

## Features

- **Injects "Add to Watchlist" button** on IMDb movie/TV show pages
- **Popup interface** for quick watchlist management
- **Real-time status checking** - shows if a movie is already in your watchlist
- **Seamless integration** with your Movie Log web application
- **Clean, modern UI** that matches IMDb's design language

## Installation

### Development Installation

1. Clone this repository
2. Open Chrome and navigate to `chrome://extensions/`
3. Enable "Developer mode" in the top right
4. Click "Load unpacked"
5. Select the `browser-extension` folder from this repository

### Configuration

Before using the extension, make sure:

1. Your Movie Log web application is running (default: `http://localhost:5000`)
2. Update the `API_BASE_URL` in the following files if your app runs on a different port:
   - `content/imdb-injector.js`
   - `popup/popup.js`

## Usage

### On IMDb Pages

1. Navigate to any movie or TV show page on IMDb
2. Look for the blue "Add to Watchlist" button near the title
3. Click to add the movie to your watchlist
4. The button will turn green and show "In Watchlist" if already added

### Extension Popup

1. Click the Movie Log icon in your browser toolbar
2. The popup will show the current movie if you're on an IMDb page
3. Add/remove movies from your watchlist
4. Click "Open Movie Log App" to view your full watchlist

## API Endpoints

The extension communicates with your Movie Log app via these endpoints:

- `POST /api/watchlist/add` - Add a movie to watchlist
- `POST /api/watchlist/remove` - Remove a movie from watchlist  
- `GET /api/watchlist/check/{imdbId}` - Check if movie is in watchlist

## File Structure

```
browser-extension/
├── manifest.json           # Extension configuration
├── content/
│   ├── imdb-injector.js    # Main content script for IMDb pages
│   └── imdb-styles.css     # Styles for injected elements
├── popup/
│   ├── popup.html          # Popup interface
│   └── popup.js            # Popup logic
├── icons/                  # Extension icons (16x16, 48x48, 128x128)
└── README.md              # This file
```

## Development

### Making Changes

1. Edit the relevant files in the extension folder
2. Go to `chrome://extensions/`
3. Click the "Reload" button for the Movie Log extension
4. Test your changes on IMDb pages

### Debugging

1. Open IMDb movie page
2. Right-click the "Add to Watchlist" button and select "Inspect"
3. Check the Console tab for any errors
4. For popup issues, right-click the extension icon and select "Inspect popup"

## Security Notes

- The extension only requests permissions for IMDb and your local Movie Log app
- All data is stored locally in your Movie Log application
- No data is sent to external servers

## Compatibility

- **Chrome**: Full support
- **Firefox**: Manifest V3 support required
- **Edge**: Chrome extension compatibility

## Troubleshooting

### Button not appearing
- Check that you're on an IMDb movie/TV show page (`imdb.com/title/`)
- Verify the extension is enabled in `chrome://extensions/`
- Check browser console for errors

### API errors
- Ensure your Movie Log app is running
- Verify the API URL in the extension files matches your app URL
- Check browser network tab for failed requests

### Permission issues
- Make sure the extension has the necessary permissions
- Try disabling and re-enabling the extension

## Contributing

Feel free to submit issues and enhancement requests!
