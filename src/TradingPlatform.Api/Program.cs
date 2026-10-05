using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using Serilog;
using Scalar.AspNetCore;
using TradingPlatform.Api.Hosting;
using TradingPlatform.Api.Middleware;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    TradingPlatform.Application.Trading.TradingHostConfiguration.RejectUnsupportedMode(builder.Configuration);
    builder.Configuration.AddInMemoryCollection(TradingPlatform.Application.Trading.LocalSecrets.Resolve(builder.Configuration));

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .WriteTo.Console()
            .WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day));

    SecretsGuard.Enforce(builder.Configuration, builder.Environment, Log.Logger);
    builder.Services.AddTradingPlatformApi(builder.Configuration);

    builder.Services.AddControllers(options => options.Conventions.Add(new OperatorMutationsConvention()))
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    var app = builder.Build();

    await app.Services.InitializeDatabaseAsync(app.Environment, app.Lifetime.ApplicationStopping);

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    app.UseSerilogRequestLogging();

    var openApi = app.MapOpenApi();
    if (app.Environment.IsDevelopment())
    {
        openApi.AllowAnonymous();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Trading Platform API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "Trading Platform API";
        });
        app.MapScalarApiReference().AllowAnonymous();
        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription().AllowAnonymous();
    }

    app.UseCors("Default");
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<TradingPlatform.Api.Hubs.TradingHub>(TradingPlatform.Api.Hubs.TradingHub.Route);
    app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live")
    }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    }).AllowAnonymous();
    app.MapHealthChecks("/health").AllowAnonymous();

    Log.Information(
        "Trading mode banner: Venue={Venue} LiveTradingEnabled={Live} KillSwitchEnabled={Kill} HostBotEngine={Host} Environment={Env}",
        TradingPlatform.Application.Trading.TradingVenue.Read(app.Configuration),
        app.Configuration.GetValue<bool>("Trading:LiveTradingEnabled"),
        app.Configuration.GetValue<bool>("Trading:KillSwitchEnabled"),
        app.Configuration.GetValue("Trading:HostBotEngine", true),
        app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Trading Platform API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
