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
        string timeZoneId =
            configuration["Business:TimeZoneId"]
            ?? throw new InvalidOperationException("Business:TimeZoneId is required.");
        services.TryAddSingleton(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        return services;
    }
}
