using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Identity.Api.Write;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<IdentityWriteDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("IdentityWrite"))
);
builder
    .Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<IdentityWriteDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder.Services.AddScoped<AccessTokenService>();
builder.Services.AddScoped<IdentitySeed>();

WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    IdentityWriteDbContext db = scope.ServiceProvider.GetRequiredService<IdentityWriteDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
    {
        await scope.ServiceProvider.GetRequiredService<IdentitySeed>().SeedAsync();
    }
}

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapIdentityEndpoints();
app.Run();
