using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Trading;

public sealed class BotEngineWorker : BackgroundService
{
    /// <summary>A burst of exchange events (one fill can send several) runs one cycle, not one per event.</summary>
    public static readonly TimeSpan MinWakeGap = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly TradingOptions _options;
    private readonly IExchangeEventSignal _events;
    private readonly ILogger<BotEngineWorker> _logger;

    public BotEngineWorker(IServiceScopeFactory scopes, IOptions<TradingOptions> options, IExchangeEventSignal events, ILogger<BotEngineWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _events = events;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Bot engine started. Interval {Interval}s. Auto-start disabled. Live orders only after a coin is started manually.",
            _options.BotEngineIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                using var scope = _scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<IWorkerLease>().HoldAsync(WorkerLeaseNames.BotEngine, stoppingToken))
                {
                    var engine = scope.ServiceProvider.GetRequiredService<IBotEngine>();
                    await engine.EvaluateRunningBotsAsync(stoppingToken);
                }
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
                await WaitForNextCycleAsync(started, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task WaitForNextCycleAsync(DateTimeOffset started, CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.BotEngineIntervalSeconds));
        if (!await _events.WaitAsync(interval, stoppingToken))
        {
            return;
        }

        var gap = MinWakeGap - (DateTimeOffset.UtcNow - started);
        if (gap > TimeSpan.Zero)
        {
            await Task.Delay(gap, stoppingToken);
        }
    }
}
