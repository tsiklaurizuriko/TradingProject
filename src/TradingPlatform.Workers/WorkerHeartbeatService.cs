using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingPlatform.Workers;

public sealed class WorkerHeartbeatService : BackgroundService
{
    private readonly ILogger<WorkerHeartbeatService> _logger;

    public WorkerHeartbeatService(ILogger<WorkerHeartbeatService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Trading platform worker host started. Live trading remains disabled unless explicitly enabled.");
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Worker host heartbeat at {UtcNow}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
