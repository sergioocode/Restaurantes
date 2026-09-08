using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace Restaurantes.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();

        return builder;
    }

    public static WebApplication MapServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.MapHealthChecks("/health", new HealthCheckOptions());
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = _ => false });

        return app;
    }
}
