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
builder.Services.AddScoped<GistSyncService>();
builder.Services.AddScoped<AutoSyncService>();
builder.Services.AddScoped<OpenOnLinkService>();
var host = builder.Build();

await host.RunAsync();
