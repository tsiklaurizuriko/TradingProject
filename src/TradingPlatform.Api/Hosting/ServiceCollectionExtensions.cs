using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TradingPlatform.Api.Health;
using TradingPlatform.Api.Hubs;
using TradingPlatform.Application;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Backtesting;
using TradingPlatform.Binance;
using TradingPlatform.Execution;
using TradingPlatform.Infrastructure;
using TradingPlatform.MarketData;
using TradingPlatform.Risk;
using TradingPlatform.Strategies;
using TradingPlatform.Trading;

namespace TradingPlatform.Api.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTradingPlatformApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:4200"];

        services.AddCors(options =>
        {
            options.AddPolicy("Default", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        services.AddOpenApi();

        services.AddHttpContextAccessor();
        services.AddSignalR();

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"])
            .AddCheck<ComponentHealthCheck>("trading-components", tags: ["ready"]);

        services.AddApplication(configuration);
        services.AddInfrastructure(configuration);
        AddJwtAuthentication(services, configuration);
        services.AddStrategies();
        services.AddRisk();
        services.AddExecution();
        services.AddMarketData();
        services.AddTrading(configuration);
        services.AddBacktesting();
        services.AddBinance(configuration);
        services.AddSingleton<ITradingRealtimePublisher, SignalRTradingRealtimePublisher>();

        var otlp = configuration["OpenTelemetry:OtlpEndpoint"];
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("TradingPlatform.Api"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(otlp))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddPrometheusExporter();
            });

        return services;
    }

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var signingKey = configuration["Jwt:SigningKey"] ?? "CHANGE-ME-to-a-long-random-signing-key-32chars-min";
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "TradingPlatform",
                    ValidAudience = configuration["Jwt:Audience"] ?? "TradingPlatform",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();
    }
}
