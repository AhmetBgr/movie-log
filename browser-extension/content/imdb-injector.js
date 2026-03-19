// Movie Log IMDb Integration
(function() {
    'use strict';

    // Configuration
    const CONFIG = {
        STORAGE_KEY: 'my_movie_list',
        BUTTON_ID: 'movie-log-add-btn',
        BUTTON_CLASS: 'movie-log-btn'
    };

    // State
    let isAdded = false;
    let imdbId = '';
    let movieData = {};

    // Initialize
    function init() {
        console.log('Movie Log: Initializing extension...');
        extractImdbData();
        console.log('Movie Log: Extracted data:', movieData);
        if (imdbId) {
            console.log('Movie Log: IMDb ID found:', imdbId);
            checkIfInWatchlist();
            injectButton();
        } else {
            console.error('Movie Log: No IMDb ID found');
        }
    }

    // Extract movie data from IMDb page
    function extractImdbData() {
        // Get IMDb ID from URL
        const urlMatch = window.location.pathname.match(/\/title\/(tt\d+)\//);
        if (urlMatch) {
            imdbId = urlMatch[1];
        }

        // Extract title
        const titleElement = document.querySelector('[data-testid="hero__primary-text"] h1') || 
                           document.querySelector('h1[data-testid="hero__pageTitle"]') ||
                           document.querySelector('h1');
        const title = titleElement?.textContent?.trim() || '';

        // Extract year
        const yearElement = document.querySelector('[data-testid="title-details-certificate"]') ||
                          document.querySelector('.sc-afe28a6-0');
        const yearMatch = yearElement?.textContent?.match(/\d{4}/);
        const year = yearMatch ? yearMatch[0] : '';

        // Extract type (Movie/TV Series)
        const typeElement = document.querySelector('[data-testid="hero-title-block__metadata"]') ||
                           document.querySelector('.title-type');
        const typeText = typeElement?.textContent?.trim() || '';
        const type = typeText.includes('TV') ? 'TV Series' : 'Movie';

        // Extract genres
        const genreElements = document.querySelectorAll('[data-testid="genres"] a, .ipc-chip-list__item a');
        const genres = Array.from(genreElements).map(el => el.textContent.trim()).join(', ') || '';

        // Extract director
        const directorElement = document.querySelector('[data-testid="title-pc-wide-screen"] a[href*="/name/nm"]') ||
                             document.querySelector('.ipc-metadata-list-item a[href*="/name/nm"]');
        const director = directorElement?.textContent?.trim() || '';

        // Extract poster
        const posterElement = document.querySelector('[data-testid="hero-media-poster"] img') ||
                            document.querySelector('.ipc-image img');
        const poster = posterElement?.src || '';

        // Extract rating
        const ratingElement = document.querySelector('[data-testid="hero-rating-bar__aggregate-rating__score"] span');
        const rating = ratingElement?.textContent?.trim() || '';

        movieData = {
            imdbId,
            title,
            year,
            type,
            genres,
            director,
            poster,
            rating
        };
    }

    // Check if movie is already in watchlist
    async function checkIfInWatchlist() {
        try {
            const response = await chrome.runtime.sendMessage({
                action: 'checkMovieLog',
                imdbId: imdbId
            });
            
            if (response && response.exists !== undefined) {
                isAdded = response.exists || response.isInWatchlist;
                updateButtonState();
            }
        } catch (error) {
            console.error('Movie Log: Error checking watchlist:', error);
        }
    }

    // Get watchlist from localStorage
    function getWatchlistFromStorage() {
        try {
            const json = localStorage.getItem(CONFIG.STORAGE_KEY);
            console.log('Movie Log: Reading from localStorage key:', CONFIG.STORAGE_KEY);
            console.log('Movie Log: Raw localStorage data:', json);
            const data = json ? JSON.parse(json) : [];
            console.log('Movie Log: Parsed watchlist length:', data.length);
            return data;
        } catch (error) {
            console.error('Movie Log: Error reading from localStorage:', error);
            return [];
        }
    }

    // Save watchlist to localStorage
    function saveWatchlistToStorage(watchlist) {
        try {
            console.log('Movie Log: Saving to localStorage key:', CONFIG.STORAGE_KEY);
            console.log('Movie Log: Watchlist to save:', watchlist);
            const json = JSON.stringify(watchlist);
            console.log('Movie Log: JSON string length:', json.length);
            localStorage.setItem(CONFIG.STORAGE_KEY, json);
            console.log('Movie Log: Successfully saved to localStorage');
            
            // Verify it was saved
            const verify = localStorage.getItem(CONFIG.STORAGE_KEY);
            console.log('Movie Log: Verification - saved data length:', verify ? verify.length : 0);
            
            // Trigger storage event to notify other tabs/pages
            window.dispatchEvent(new StorageEvent('storage', {
                key: CONFIG.STORAGE_KEY,
                newValue: json,
                oldValue: null,
                storageArea: localStorage
            }));
            console.log('Movie Log: Dispatched storage event');
        } catch (error) {
            console.error('Movie Log: Error saving to localStorage:', error);
        }
    }

    // Inject button into IMDb page
    function injectButton() {
        // Find the appropriate location to inject the button
        const selectors = [
            '[data-testid="hero__primary-text"]',
            'h1[data-testid="hero__pageTitle"]',
            'h1',
            '.title_wrapper h1',
            '.hero__primary-text'
        ];

        let targetContainer = null;
        for (const selector of selectors) {
            const element = document.querySelector(selector);
            if (element && element.parentElement) {
                targetContainer = element.parentElement;
                break;
            }
        }

        if (!targetContainer) {
            console.error('Movie Log: Could not find target container for button injection');
            return;
        }

        // Remove existing button if present
        const existingButton = document.getElementById('movie-log-watchlist-btn');
        if (existingButton) {
            existingButton.remove();
        }

        // Create button container
        const buttonContainer = document.createElement('div');
        buttonContainer.style.cssText = `
            margin: 10px 0;
            display: flex;
            align-items: center;
            gap: 10px;
        `;

        // Create and inject the button
        const button = document.createElement('button');
        button.id = 'movie-log-watchlist-btn';
        button.className = 'movie-log-watchlist-btn';
        button.textContent = isAdded ? 'Remove from Watchlist' : 'Add to Watchlist';
        
        button.style.cssText = `
            background: linear-gradient(135deg, #6366f1 0%, #4338ca 100%);
            color: white;
            border: none;
            padding: 8px 16px;
            border-radius: 8px;
            font-size: 14px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s ease;
            display: flex;
            align-items: center;
            gap: 8px;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
        `;

        updateButtonContent(button);

        // Add hover effects
        button.addEventListener('mouseenter', () => {
            button.style.transform = 'translateY(-1px)';
            button.style.boxShadow = '0 4px 12px rgba(99, 102, 241, 0.3)';
        });

        button.addEventListener('mouseleave', () => {
            button.style.transform = 'translateY(0)';
            button.style.boxShadow = 'none';
        });

        // Add click handler
        button.addEventListener('click', handleButtonClick);

        // Add status indicator
        const statusIndicator = document.createElement('span');
        statusIndicator.style.cssText = `
            font-size: 12px;
            opacity: 0.8;
            margin-left: 8px;
        `;

        buttonContainer.appendChild(button);
        buttonContainer.appendChild(statusIndicator);

        // Insert into the page
        targetContainer.appendChild(buttonContainer);

        // Store reference for updates
        window.movieLogButton = button;
        window.movieLogStatus = statusIndicator;
    }

    function updateButtonContent(button) {
        const icon = isAdded ? '✓' : '+';
        const text = isAdded ? 'In Watchlist' : 'Add to Watchlist';
        
        button.innerHTML = `
            <span style="font-size: 16px;">${icon}</span>
            <span>${text}</span>
        `;

        if (isAdded) {
            button.style.background = 'linear-gradient(135deg, #10b981 0%, #059669 100%)';
        } else {
            button.style.background = 'linear-gradient(135deg, #6366f1 0%, #4338ca 100%)';
        }
    }

    function updateButtonState() {
        const button = document.getElementById('movie-log-watchlist-btn');
        if (button) {
            updateButtonContent(button);
        }
    }

    // Handle button click
    async function handleButtonClick() {
        const button = document.getElementById('movie-log-watchlist-btn');
        const statusIndicator = window.movieLogStatus;

        if (!button || !imdbId) return;

        console.log('Movie Log: Button clicked, isAdded:', isAdded);
        console.log('Movie Log: Movie data:', movieData);

        // Disable button during operation
        button.disabled = true;
        button.style.opacity = '0.7';

        try {
            if (isAdded) {
                // Remove from watchlist
                console.log('Movie Log: Removing from watchlist...');
                
                const response = await chrome.runtime.sendMessage({
                    action: 'saveToMovieLog',
                    movie: {
                        ImdbId: movieData.imdbId,
                        action: 'remove'
                    }
                });

                console.log('Movie Log: Remove response:', response);
                if (response && response.success) {
                    isAdded = false;
                    updateButtonState();
                    showStatus('Removed from watchlist', 'success');
                } else {
                    throw new Error('Failed to remove from watchlist');
                }
            } else {
                // Add to watchlist
                console.log('Movie Log: Adding to watchlist...');
                
                // Create new watchlist item matching your app's exact format
                const newItem = {
                    ImdbId: movieData.imdbId,
                    Title: movieData.title,
                    TitleType: movieData.type || "Movie",
                    Year: movieData.year || "",
                    Genres: movieData.genres || null,
                    Director: movieData.director || "",
                    OriginalTitle: null,
                    ParsedYear: parseInt(movieData.year) || 0,
                    Status: 0, // Pending
                    CurrentSeason: null,
                    CurrentEpisode: null,
                    DateAdded: new Date().toISOString().slice(0, 10), // Format: YYYY-MM-DD
                    UserRating: null,
                    Rating20: null,
                    Overview: null,
                    DisplayOriginalTitle: null,
                    action: 'add'
                };
                
                console.log('Movie Log: Add payload:', newItem);
                
                const response = await chrome.runtime.sendMessage({
                    action: 'saveToMovieLog',
                    movie: newItem
                });

                console.log('Movie Log: Add response:', response);

                if (response && response.success) {
                    isAdded = true;
                    updateButtonState();
                    showStatus('Added to watchlist!', 'success');
                } else {
                    throw new Error('Failed to add to watchlist');
                }
            }
        } catch (error) {
            console.error('Movie Log: Error:', error);
            showStatus('Error occurred', 'error');
        } finally {
            // Re-enable button
            button.disabled = false;
            button.style.opacity = '1';
        }
    }

    function showStatus(message, type) {
        const statusIndicator = window.movieLogStatus;
        if (statusIndicator) {
            statusIndicator.textContent = message;
            statusIndicator.style.color = type === 'success' ? '#10b981' : '#ef4444';
            
            // Clear status after 3 seconds
            setTimeout(() => {
                statusIndicator.textContent = '';
            }, 3000);
        }
    }

    // Wait for page to load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Handle navigation changes (for single-page app behavior)
    let lastUrl = location.href;
    new MutationObserver(() => {
        const url = location.href;
        if (url !== lastUrl) {
            lastUrl = url;
            setTimeout(init, 1000); // Reinitialize after navigation
        }
    }).observe(document, { subtree: true, childList: true });

})();
