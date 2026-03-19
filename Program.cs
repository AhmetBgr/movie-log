using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MyPrivateWatchlist;
using MyPrivateWatchlist.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<WatchlistService>();
var host = builder.Build();

var watchlistSvc = host.Services.GetRequiredService<WatchlistService>();
await watchlistSvc.InitializeAsync();

await host.RunAsync();
