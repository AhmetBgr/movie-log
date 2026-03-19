// Content script for Movie Log website
(function() {
    'use strict';

    console.log('Movie Log: Content script loaded on Movie Log website');

    // Listen for messages from background script
    chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
        console.log('Movie Log: Received message:', request);

        if (request.action === 'addToWatchlist') {
            try {
                const watchlist = JSON.parse(localStorage.getItem('my_movie_list') || '[]');
                
                if (request.movie.action === 'add') {
                    // Check if movie already exists
                    const existing = watchlist.find(m => m.ImdbId === request.movie.ImdbId);
                    if (existing) {
                        sendResponse({success: false, message: "Movie already in watchlist"});
                        return;
                    }
                    
                    // Add new movie
                    watchlist.push(request.movie);
                    localStorage.setItem('my_movie_list', JSON.stringify(watchlist));
                    console.log('Movie Log: Added movie to watchlist:', request.movie.Title);
                    
                    // Trigger a refresh of the watchlist if the app supports it
                    window.dispatchEvent(new CustomEvent('watchlistUpdated', {detail: request.movie}));
                    
                    sendResponse({success: true, message: "Movie added to watchlist"});
                } else if (request.movie.action === 'remove') {
                    // Remove movie
                    const index = watchlist.findIndex(m => m.ImdbId === request.movie.ImdbId);
                    if (index !== -1) {
                        const removed = watchlist.splice(index, 1)[0];
                        localStorage.setItem('my_movie_list', JSON.stringify(watchlist));
                        console.log('Movie Log: Removed movie from watchlist:', removed.Title);
                        
                        // Trigger a refresh of the watchlist if the app supports it
                        window.dispatchEvent(new CustomEvent('watchlistUpdated', {detail: {action: 'remove', movie: removed}}));
                        
                        sendResponse({success: true, message: "Movie removed from watchlist"});
                    } else {
                        sendResponse({success: false, message: "Movie not found in watchlist"});
                    }
                }
            } catch (error) {
                console.error('Movie Log: Error handling watchlist operation:', error);
                sendResponse({success: false, error: error.message});
            }
        } else if (request.action === 'checkWatchlist') {
            try {
                const watchlist = JSON.parse(localStorage.getItem('my_movie_list') || '[]');
                const movie = watchlist.find(m => m.ImdbId === request.imdbId);
                
                sendResponse({
                    exists: !!movie,
                    isInWatchlist: !!movie,
                    item: movie
                });
            } catch (error) {
                console.error('Movie Log: Error checking watchlist:', error);
                sendResponse({exists: false, isInWatchlist: false});
            }
        }

        return true; // Keep the message channel open for async response
    });

    console.log('Movie Log: Content script ready');
})();
