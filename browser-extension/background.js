// Background script for cross-domain communication
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === 'saveToMovieLog') {
        console.log('Background: Looking for Movie Log tabs...');
        
        // List all tabs to debug
        chrome.tabs.query({}, (allTabs) => {
            console.log('Background: All open tabs:', allTabs.map(tab => ({url: tab.url, title: tab.title})));
        });
        
        // Send message to the Movie Log website
        chrome.tabs.query({url: "*://localhost/*"}, (tabs) => {
            console.log('Background: Found Movie Log tabs:', tabs.length);
            
            // Filter for the specific port (7008)
            const movieLogTabs = tabs.filter(tab => tab.url.includes('localhost:7008'));
            console.log('Background: Found tabs with port 7008:', movieLogTabs.length);
            
            if (movieLogTabs.length > 0) {
                console.log('Background: Sending message to tab:', movieLogTabs[0].id, movieLogTabs[0].url);
                chrome.tabs.sendMessage(movieLogTabs[0].id, {
                    action: 'addToWatchlist',
                    movie: request.movie
                }, (response) => {
                    console.log('Background: Received response:', response);
                    sendResponse(response);
                });
            } else {
                console.log('Background: No Movie Log tabs found on port 7008');
                sendResponse({success: false, error: 'Movie Log website not open'});
            }
        });
        return true; // Keep the message channel open for async response
    }
    
    if (request.action === 'checkMovieLog') {
        console.log('Background: Checking movie in Movie Log...');
        
        chrome.tabs.query({url: "*://localhost/*"}, (tabs) => {
            console.log('Background: Found', tabs.length, 'Movie Log tabs for check');
            
            // Filter for the specific port (7008)
            const movieLogTabs = tabs.filter(tab => tab.url.includes('localhost:7008'));
            console.log('Background: Found tabs with port 7008:', movieLogTabs.length);
            
            if (movieLogTabs.length > 0) {
                chrome.tabs.sendMessage(movieLogTabs[0].id, {
                    action: 'checkWatchlist',
                    imdbId: request.imdbId
                }, (response) => {
                    console.log('Background: Check response:', response);
                    sendResponse(response);
                });
            } else {
                console.log('Background: No Movie Log tabs found on port 7008');
                sendResponse({exists: false, isInWatchlist: false});
            }
        });
        return true;
    }
});
