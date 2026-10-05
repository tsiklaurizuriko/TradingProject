using Microsoft.Extensions.Configuration;

namespace TradingPlatform.Application.Trading;

public static class TradingHostConfiguration
{
    public static void RejectUnsupportedMode(IConfiguration configuration)
    {
        var mode = configuration["Trading:DefaultMode"];
        if (!string.IsNullOrWhiteSpace(mode)
            && !string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Trading:DefaultMode is '{mode}'. Only Live is supported. This process will not start, and the value was not treated as live.");
        }

        TradingVenue.Validate(configuration);
        if (TradingVenue.Read(configuration) == TradingVenueKind.Shadow
            && string.Equals(configuration["Trading:HostBotEngine"], "false", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Trading:Venue is Shadow but this process does not host the bot engine. The simulated account lives in the engine's process; run Shadow as one process with Trading:HostBotEngine=true.");
        }
    }

    public const string PlaceholderEncryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>Live trading with the committed placeholder credential key would let anyone with the repo decrypt stored Binance keys.</summary>
    public static void RejectPlaceholderSecretsWhenLive(IConfiguration configuration)
    {
        if (!string.Equals(configuration["Trading:LiveTradingEnabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = configuration["Credentials:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(key) || string.Equals(key, PlaceholderEncryptionKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Trading:LiveTradingEnabled is true but Credentials:EncryptionKey is missing or the committed placeholder. Set Credentials__EncryptionKey.");
        }
    }
}
