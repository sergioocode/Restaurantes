using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Restaurantes.Clients.Pos.Pwa;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost:5001") });
builder.Services.AddScoped<PosApi>();
builder.Services.AddScoped<OrderRealtimeClient>();
builder.Services.AddScoped<DiningRealtimeClient>();

await builder.Build().RunAsync();
