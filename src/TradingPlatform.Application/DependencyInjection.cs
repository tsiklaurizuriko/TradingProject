using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Auth;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
        if (configuration is not null)
        {
            services.Configure<ScannerOptions>(configuration.GetSection(ScannerOptions.SectionName));
        }

        return services;
    }
}
