using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        var signedFuturesUrl = configuration["Binance:FuturesSignedRestBaseUrl"] ?? futuresUrl;
        var venue = TradingVenue.Read(configuration);
        services.AddHttpClient("binance-futures", client =>
        {
            client.BaseAddress = new Uri(futuresUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });
        services.AddHttpClient(BinanceSignedRestClient.SignedFuturesClientName, client =>
        {
            client.BaseAddress = new Uri(signedFuturesUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });
        services.AddTransient<IPublicMarketDataClient, BinancePublicMarketDataClient>();
        services.AddHttpClient<BinanceSignedRestClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });
        services.TryAddSingleton<IExchangeEventSignal, ExchangeEventSignal>();
        if (configuration.GetValue("Binance:MarketStream:Enabled", false))
        {
            services.AddHostedService<BinanceMarketStreamWorker>();
        }

        if (venue == TradingVenueKind.Shadow)
        {
            return services;
        }

        services.AddScoped<ILiveExchangeConnectorFactory, BinanceLiveExchangeConnectorFactory>();
        services.AddScoped<IExchangeAccountService, ExchangeAccountService>();
        if (configuration.GetValue("Trading:HostBotEngine", true) && configuration.GetValue("Binance:UserDataStream:Enabled", true))
        {
            services.AddHostedService<BinanceUserDataStreamWorker>();
        }

        return services;
    }
}
