using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Auth;

namespace TradingPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
        return services;
    }
}
