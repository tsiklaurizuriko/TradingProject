using Microsoft.Extensions.Configuration;

namespace TradingPlatform.Application.Trading;

/// <summary>
/// Where this process sends orders. One process serves one venue, and Shadow or Testnet must use their own
/// database, so positions, orders, balances, worker leases and the reconciler never mix with Live.
/// </summary>
public enum TradingVenueKind
{
    Live = 0,
    /// <summary>Simulated fills on real Binance book prices. Never calls the signed API.</summary>
    Shadow = 1,
    /// <summary>Binance USD-M testnet with its own keys. Market data stays on mainnet.</summary>
    Testnet = 2
}

public static class TradingVenue
{
    public const string ConfigKey = "Trading:Venue";

    public static TradingVenueKind Read(IConfiguration configuration) => Parse(configuration[ConfigKey]);

    public static TradingVenueKind Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TradingVenueKind.Live;
        }

        return Enum.TryParse<TradingVenueKind>(value.Trim(), ignoreCase: true, out var venue) && Enum.IsDefined(venue)
            ? venue
            : throw new InvalidOperationException($"{ConfigKey} is '{value}'. Use Live, Shadow, or Testnet.");
    }

    /// <summary>Throws when a Shadow or Testnet process could write into the Live database or reach mainnet order endpoints.</summary>
    public static void Validate(IConfiguration configuration)
    {
        var venue = Read(configuration);
        if (venue == TradingVenueKind.Live)
        {
            return;
        }

        var database = DatabaseName(configuration.GetConnectionString("TradingPlatform"));
        var marker = venue.ToString().ToLowerInvariant();
        if (database is null || !database.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ConfigKey} is {venue} but the database is '{database ?? "(none)"}'. Use a separate database whose name contains '{marker}' so {venue} rows never mix with Live.");
        }

        if (venue != TradingVenueKind.Testnet)
        {
            return;
        }

        var rest = configuration["Binance:FuturesSignedRestBaseUrl"];
        if (!IsTestnetHost(rest))
        {
            throw new InvalidOperationException(
                "Trading:Venue is Testnet but Binance:FuturesSignedRestBaseUrl is not a Binance testnet host (https://testnet.binancefuture.com). Orders would reach mainnet.");
        }

        var socket = configuration["Binance:FuturesWebSocketBaseUrl"];
        if (configuration.GetValue("Binance:UserDataStream:Enabled", true) && !IsTestnetHost(socket))
        {
            throw new InvalidOperationException(
                "Trading:Venue is Testnet but Binance:FuturesWebSocketBaseUrl is not the testnet stream (wss://stream.binancefuture.com).");
        }
    }

    public static bool IsTestnetHost(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("testnet.binancefuture.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("stream.binancefuture.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("fstream.binancefuture.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".binancefuture.com", StringComparison.OrdinalIgnoreCase));

    public static string? DatabaseName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq].Trim();
            if (key.Equals("Database", StringComparison.OrdinalIgnoreCase) || key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
            {
                return part[(eq + 1)..].Trim();
            }
        }

        return null;
    }
}
