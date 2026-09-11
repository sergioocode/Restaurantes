using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Restaurantes.Clients.Commander.Pwa;
using Restaurantes.Clients.Commander.Pwa.Api;
using Restaurantes.Clients.Commander.Pwa.Realtime;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

Uri applicationUri = new(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(applicationUri.GetLeftPart(UriPartial.Authority))
});
builder.Services.AddScoped<CommanderApi>();
builder.Services.AddScoped<OrderRealtimeClient>();
builder.Services.AddScoped<DiningRealtimeClient>();

await builder.Build().RunAsync();
