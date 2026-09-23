using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Trading;

public sealed class PriceActionArmLoader : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TradingOptions _options;
    private readonly ILogger<PriceActionArmLoader> _logger;

    public PriceActionArmLoader(
        IServiceScopeFactory scopes,
        IOptions<TradingOptions> options,
        ILogger<PriceActionArmLoader> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ITradingStore>();
            var settings = await store.GetSettingsAsync("Trading.PriceAction.", cancellationToken);
            PriceActionArm.Apply(_options.PriceAction, settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Near-miss arm settings stayed at the configured defaults.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
