// Movie Log Extension Popup
(function() {
    'use strict';

    const CONFIG = {
        API_BASE_URL: 'http://localhost:5000',
        APP_URL: 'http://localhost:5000'
    };

    let currentMovie = null;
    let isInWatchlist = false;

    // Initialize popup
    async function init() {
        try {
            await getCurrentTabMovie();
            if (currentMovie) {
                await checkWatchlistStatus();
                renderMovieInfo();
            } else {
                renderNoMovie();
            }
        } catch (error) {
            console.error('Popup initialization error:', error);
            renderError('Failed to load movie information');
        }
    }

    // Get current tab and extract movie info
    async function getCurrentTabMovie() {
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        
        if (!tab.url || !tab.url.includes('imdb.com/title/')) {
            return null;
        }

        // Extract IMDb ID from URL
        const urlMatch = tab.url.match(/\/title\/(tt\d+)\//);
        if (!urlMatch) {
            return null;
        }

        const imdbId = urlMatch[1];

        // Try to get movie data from content script
        try {
            const response = await chrome.tabs.sendMessage(tab.id, { action: 'getMovieData' });
            if (response && response.movieData) {
                currentMovie = response.movieData;
                currentMovie.imdbId = imdbId;
                return currentMovie;
            }
        } catch (error) {
            console.log('Could not get data from content script, using fallback');
        }

        // Fallback: Extract basic info from page title
        const title = tab.title.replace(' - IMDb', '').trim();
        const yearMatch = title.match(/\((\d{4})\)/);
        const year = yearMatch ? yearMatch[1] : '';
        const cleanTitle = title.replace(/\s*\(\d{4}\)\s*$/, '');

        currentMovie = {
            imdbId,
            title: cleanTitle,
            year,
            type: 'Movie' // Default assumption
        };

        return currentMovie;
    }

    // Check if movie is in watchlist
    async function checkWatchlistStatus() {
        if (!currentMovie || !currentMovie.imdbId) return;

        try {
            const response = await fetch(`${CONFIG.API_BASE_URL}/api/watchlist/check/${currentMovie.imdbId}`);
            if (response.ok) {
                const data = await response.json();
                isInWatchlist = data.exists || data.isInWatchlist;
            }
        } catch (error) {
            console.error('Error checking watchlist status:', error);
        }
    }

    // Render movie information
    function renderMovieInfo() {
        const content = document.getElementById('content');
        
        content.innerHTML = `
            <div class="current-page">
                <h3>Current Page</h3>
                <div class="movie-title">${escapeHtml(currentMovie.title || 'Unknown Title')}</div>
                <div class="movie-meta">
                    ${currentMovie.year ? `${currentMovie.year} • ` : ''}${currentMovie.type || 'Movie'}
                    ${currentMovie.genres ? ` • ${currentMovie.genres}` : ''}
                </div>
                <button id="actionBtn" class="action-button ${isInWatchlist ? 'remove' : 'add'}" 
                        onclick="handleActionClick()">
                    ${isInWatchlist ? '✓ In Watchlist' : '+ Add to Watchlist'}
                </button>
            </div>
        `;
    }

    // Render no movie state
    function renderNoMovie() {
        const content = document.getElementById('content');
        
        content.innerHTML = `
            <div class="no-movie">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                          d="M7 4v16M17 4v16M3 8h4m10 0h4M3 16h4m10 0h4" />
                </svg>
                <div>No movie found</div>
                <div style="font-size: 12px; margin-top: 8px;">
                    Navigate to an IMDb movie page to add it to your watchlist
                </div>
            </div>
        `;
    }

    // Render error state
    function renderError(message) {
        const content = document.getElementById('content');
        
        content.innerHTML = `
            <div class="status error">
                ${escapeHtml(message)}
            </div>
        `;
    }

    // Handle button click
    async function handleActionClick() {
        const button = document.getElementById('actionBtn');
        if (!button || !currentMovie) return;

        // Disable button during operation
        button.disabled = true;
        const originalText = button.textContent;
        button.textContent = 'Processing...';

        try {
            if (isInWatchlist) {
                // Remove from watchlist
                const response = await fetch(`${CONFIG.API_BASE_URL}/api/watchlist/remove`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify({ imdbId: currentMovie.imdbId })
                });

                if (response.ok) {
                    isInWatchlist = false;
                    updateButton();
                    showStatus('Removed from watchlist', 'success');
                } else {
                    throw new Error('Failed to remove from watchlist');
                }
            } else {
                // Add to watchlist
                const response = await fetch(`${CONFIG.API_BASE_URL}/api/watchlist/add`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify({
                        imdbId: currentMovie.imdbId,
                        title: currentMovie.title,
                        titleType: currentMovie.type,
                        year: currentMovie.year
                    })
                });

                if (response.ok) {
                    isInWatchlist = true;
                    updateButton();
                    showStatus('Added to watchlist!', 'success');
                } else {
                    throw new Error('Failed to add to watchlist');
                }
            }
        } catch (error) {
            console.error('Action error:', error);
            showStatus('Error occurred', 'error');
        } finally {
            // Restore button
            button.disabled = false;
            button.textContent = originalText;
        }
    }

    // Update button state
    function updateButton() {
        const button = document.getElementById('actionBtn');
        if (button) {
            button.className = `action-button ${isInWatchlist ? 'remove' : 'add'}`;
            button.textContent = isInWatchlist ? '✓ In Watchlist' : '+ Add to Watchlist';
        }
    }

    // Show status message
    function showStatus(message, type) {
        const content = document.getElementById('content');
        const statusDiv = document.createElement('div');
        statusDiv.className = `status ${type}`;
        statusDiv.textContent = message;
        
        // Insert at the beginning of content
        content.insertBefore(statusDiv, content.firstChild);
        
        // Remove after 3 seconds
        setTimeout(() => {
            if (statusDiv.parentNode) {
                statusDiv.parentNode.removeChild(statusDiv);
            }
        }, 3000);
    }

    // Escape HTML to prevent XSS
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Handle "Open App" link
    document.getElementById('openApp').addEventListener('click', (e) => {
        e.preventDefault();
        chrome.tabs.create({ url: CONFIG.APP_URL });
    });

    // Make handleActionClick available globally
    window.handleActionClick = handleActionClick;

    // Initialize when popup is loaded
    document.addEventListener('DOMContentLoaded', init);

})();
