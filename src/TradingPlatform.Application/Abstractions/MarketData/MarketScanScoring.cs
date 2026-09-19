using TradingPlatform.Application.Trading;

namespace TradingPlatform.Application.Abstractions.MarketData;

public static class MarketScanScoring
{
    public static IReadOnlyList<MarketScanRow> Rank(
        IReadOnlyList<MarketScanRow> rows,
        ScannerWeights? weights = null)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var w = weights ?? new ScannerWeights();
        var volume = Percentile(rows.Select(r => r.QuoteVolume24h).ToList());
        var trades = Percentile(rows.Select(r => (decimal)r.Trades24h).ToList());
        var volatility = Percentile(rows.Select(r => r.VolatilityPercent).ToList());
        var spread = Percentile(rows.Select(r => r.SpreadBps <= 0m ? 0m : 1m / r.SpreadBps).ToList());
        var oi = Percentile(rows.Select(r => r.OpenInterest).ToList());
        var funding = Percentile(rows.Select(r => 1m / (1m + Math.Abs(r.FundingRate) * 1000m)).ToList());
        var trend = Percentile(rows.Select(r => r.TrendQuality).ToList());
        var total = w.QuoteVolume + w.Trades24h + w.Volatility + w.Spread + w.OpenInterest + w.Funding + w.TrendQuality + w.DataQuality;
        if (total <= 0m)
        {
            total = 1m;
        }

        var scored = new List<MarketScanRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var score =
                (volume[i] * w.QuoteVolume
                 + trades[i] * w.Trades24h
                 + volatility[i] * w.Volatility
                 + spread[i] * w.Spread
                 + oi[i] * w.OpenInterest
                 + funding[i] * w.Funding
                 + trend[i] * w.TrendQuality
                 + row.DataQuality * w.DataQuality) / total;
            scored.Add(row with { ScanScore = Math.Round(score, 6, MidpointRounding.AwayFromZero) });
        }

        return scored
            .OrderByDescending(r => r.ScanScore)
            .ThenByDescending(r => r.QuoteVolume24h)
            .ThenBy(r => r.Contract.Symbol, StringComparer.Ordinal)
            .Select((r, index) => r with { ScanRank = index + 1 })
            .ToList();
    }

    public static decimal VolatilityPercent(decimal high, decimal low, decimal last)
    {
        if (last <= 0m)
        {
            return 0m;
        }

        return Math.Max(0m, (high - low) / last * 100m);
    }

    public static decimal SpreadBps(decimal bid, decimal ask)
    {
        if (bid <= 0m || ask <= 0m || ask < bid)
        {
            return 0m;
        }

        var mid = (bid + ask) / 2m;
        return mid <= 0m ? 0m : (ask - bid) / mid * 10_000m;
    }

    public static decimal DataQuality(decimal last, decimal volume, int trades, decimal tick, decimal step, decimal minQty) =>
        last > 0m && volume >= 0m && trades >= 0 && tick > 0m && step > 0m && minQty > 0m ? 1m : 0m;

    private static List<decimal> Percentile(IReadOnlyList<decimal> values)
    {
        var n = values.Count;
        var ranked = values
            .Select((value, index) => (value, index))
            .OrderBy(x => x.value)
            .ToList();
        var scores = new decimal[n];
        if (n == 1)
        {
            scores[0] = 1m;
            return [.. scores];
        }

        for (var i = 0; i < n; i++)
        {
            scores[ranked[i].index] = (decimal)i / (n - 1);
        }

        return [.. scores];
    }
}

public static class TradeEligibilityRules
{
    public static TradeEligibilityDecision Evaluate(MarketScanRow row, ScannerOptions options)
    {
        var symbol = row.Contract.Symbol;
        if (row.DataQuality < 1m)
        {
            return new TradeEligibilityDecision(symbol, Watchable: true, Eligible: false, "Missing or invalid market metadata.");
        }

        if (row.LastPrice < options.MinLastPrice)
        {
            return new TradeEligibilityDecision(symbol, true, false, "Last price is missing.");
        }

        if (row.QuoteVolume24h < options.MinQuoteVolumeUsdt)
        {
            return new TradeEligibilityDecision(symbol, true, false, $"24h quote volume is below {options.MinQuoteVolumeUsdt:0} USDT.");
        }

        if (row.Trades24h < options.MinTrades24h)
        {
            return new TradeEligibilityDecision(symbol, true, false, $"24h trades are below {options.MinTrades24h}.");
        }

        if (row.SpreadBps > 0m && row.SpreadBps > options.MaxSpreadBps)
        {
            return new TradeEligibilityDecision(symbol, true, false, $"Spread {row.SpreadBps:0.00} bps is wider than {options.MaxSpreadBps:0.00} bps.");
        }

        if (Math.Abs(row.FundingRate) > options.MaxAbsFundingRate)
        {
            return new TradeEligibilityDecision(symbol, true, false, "Funding rate is outside the configured band.");
        }

        if (row.ScanScore < options.MinScanScore)
        {
            return new TradeEligibilityDecision(symbol, true, false, $"Scan score {row.ScanScore:0.00} is below {options.MinScanScore:0.00}.");
        }

        return new TradeEligibilityDecision(symbol, true, true, "Eligible for a paper start. LIVE stays off until you enable it.");
    }
}
