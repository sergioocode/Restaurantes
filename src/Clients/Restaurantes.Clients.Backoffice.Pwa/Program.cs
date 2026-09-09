using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Restaurantes.Clients.Backoffice.Pwa;
using Restaurantes.Clients.Backoffice.Pwa.Api;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost:5001") });
builder.Services.AddScoped<BackofficeApi>();
await builder.Build().RunAsync();
