using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Restaurantes.Clients.CustomerQr.Pwa;
using Restaurantes.Clients.CustomerQr.Pwa.Api;
using Restaurantes.Clients.Shared.Operations;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

Uri applicationUri = new(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(applicationUri.GetLeftPart(UriPartial.Authority)),
});
builder.Services.AddScoped<CustomerQrApi>();
builder.Services.AddScoped(sp => new CustomerQrCatalogRealtimeClient(
    sp.GetRequiredService<HttpClient>()
));

await builder.Build().RunAsync();
