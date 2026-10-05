using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Restaurantes.ServiceDefaults;

public static class BusinessTimeZoneExtensions
{
    public static IServiceCollection AddBusinessTimeZone(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        string timeZoneId = configuration["Business:TimeZoneId"] ?? "Europe/Madrid";
        services.TryAddSingleton(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        return services;
    }
}
