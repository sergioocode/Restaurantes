using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Restaurantes.Clients.CustomerQr.Pwa;
using Restaurantes.Clients.CustomerQr.Pwa.Api;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

Uri applicationUri = new(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(applicationUri.GetLeftPart(UriPartial.Authority))
});
builder.Services.AddScoped<CustomerQrApi>();

await builder.Build().RunAsync();
