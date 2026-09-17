using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Trading;

public sealed class BotEngineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TradingOptions _options;
    private readonly ILogger<BotEngineWorker> _logger;

    public BotEngineWorker(IServiceScopeFactory scopes, IOptions<TradingOptions> options, ILogger<BotEngineWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Bot engine started. Interval {Interval}s. Auto-start disabled. Live orders only after a coin is started manually.",
            _options.BotEngineIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<IBotEngine>();
                await engine.EvaluateRunningBotsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Paper bot engine cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.BotEngineIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
