using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Binance;

public static class DependencyInjection
{
    public static IServiceCollection AddBinance(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Binance:RestBaseUrl"] ?? "https://api.binance.com";
        var timeout = configuration.GetValue("Binance:RequestTimeoutSeconds", 15);
        var futuresUrl = configuration["Binance:FuturesRestBaseUrl"] ?? "https://fapi.binance.com";
        services.AddHttpClient("binance-futures", client =>
        {
            client.BaseAddress = new Uri(futuresUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });
        services.AddTransient<IPublicMarketDataClient, BinancePublicMarketDataClient>();
        services.AddHttpClient<BinanceSignedRestClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });
        services.AddScoped<ILiveExchangeConnectorFactory, BinanceLiveExchangeConnectorFactory>();
        services.AddScoped<IExchangeAccountService, ExchangeAccountService>();
        return services;
    }
}
