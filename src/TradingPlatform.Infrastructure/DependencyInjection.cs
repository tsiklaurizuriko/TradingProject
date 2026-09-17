using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Auth;
using TradingPlatform.Application.Trading;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.Infrastructure.Security;

namespace TradingPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TradingPlatform")
            ?? "Host=localhost;Port=5432;Database=TradingProject;Username=admin;Password=admin";

        services.AddDbContext<TradingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(TradingDbContext).Assembly.FullName);
                npgsql.CommandTimeout(30);
            }));

        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<CredentialEncryptionOptions>(configuration.GetSection("Credentials"));
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value);

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TradingDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenHasher, Sha256TokenHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddScoped<IExchangeCredentialStore, ExchangeCredentialStore>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<ITradingStore, TradingStore>();

        return services;
    }
}
