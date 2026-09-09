using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Infrastructure.Integrations;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.Payments.Infrastructure.Stores;

namespace Restaurantes.Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsWriteInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<PaymentWriteDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PaymentsWrite"))
        );
        services.AddScoped<IPaymentWriteStore, PaymentWriteStore>();
        services.AddHttpClient<ICustomerDiningSessionValidator, DiningSessionValidator>(client =>
            client.BaseAddress = new Uri(
                configuration["Dining:BaseAddress"]
                    ?? throw new InvalidOperationException("Dining:BaseAddress is required.")
            )
        );
        services.AddHttpClient<IPaymentCashRegister, PaymentCashRegister>(client =>
            client.BaseAddress = new Uri(
                configuration["CashRegister:BaseAddress"] ?? "http://localhost:5105"
            )
        );
        return services;
    }
}
