using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TradingPlatform.Application;
using TradingPlatform.Backtesting;
using TradingPlatform.Binance;
using TradingPlatform.Execution;
using TradingPlatform.Infrastructure;
using TradingPlatform.MarketData;
using TradingPlatform.Risk;
using TradingPlatform.Strategies;
using TradingPlatform.Trading;
using TradingPlatform.Workers;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    TradingPlatform.Application.Trading.TradingHostConfiguration.RejectUnsupportedMode(builder.Configuration);
    builder.Configuration.AddInMemoryCollection(TradingPlatform.Application.Trading.LocalSecrets.Resolve(builder.Configuration));
    TradingPlatform.Application.Trading.TradingHostConfiguration.RejectPlaceholderSecretsWhenLive(builder.Configuration);

    builder.Services.AddSerilog((services, configuration) =>
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .WriteTo.Console()
            .WriteTo.File("logs/workers-.log", rollingInterval: RollingInterval.Day));

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddStrategies();
    builder.Services.AddRisk();
    builder.Services.AddExecution();
    builder.Services.AddMarketData();
    builder.Services.AddTrading(builder.Configuration);
    builder.Services.AddBacktesting();
    builder.Services.AddBinance(builder.Configuration);
    builder.Services.AddTradingPlatformWorkers();

    var otlp = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("TradingPlatform.Workers"))
        .WithTracing(tracing =>
        {
            tracing.AddHttpClientInstrumentation();
            if (!string.IsNullOrWhiteSpace(otlp))
            {
                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
            }
        });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Trading Platform workers terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
