using TradingPlatform.Backtesting;

namespace TradingPlatform.Research.Framework;

public enum CostProfileKind
{
    Base,
    Conservative,
    Stress
}

public enum LiquidityBucket
{
    Major,
    Mid,
    Small
}

/// <summary>Per-side costs in percent of notional. Funding is charged separately from real settlements.</summary>
public sealed record CostProfile(
    CostProfileKind Kind,
    LiquidityBucket Bucket,
    decimal FeePercent,
    decimal SlippagePercent,
    int ExecutionDelayBars,
    bool IncludeFunding)
{
    public string Label => Kind.ToString().ToUpperInvariant();

    public string Describe() =>
        $"{Label}/{Bucket}: fee {FeePercent:0.####}% + slippage {SlippagePercent:0.####}% per side, delay {ExecutionDelayBars} bar(s), funding {(IncludeFunding ? "real settlements" : "excluded")}";
}

public static class CostProfiles
{
    public const decimal TakerFeePercent = 0.05m;
    public const decimal MakerFeePercent = 0.02m;
    public const decimal StressFeePercent = 0.075m;
    public const decimal MajorQuoteVolume = 500_000_000m;
    public const decimal MidQuoteVolume = 50_000_000m;
    public const decimal MaxImpactPercent = 0.5m;

    public static IReadOnlyList<CostProfileKind> All { get; } =
        [CostProfileKind.Base, CostProfileKind.Conservative, CostProfileKind.Stress];

    public static LiquidityBucket Bucket(decimal quoteVolume24h) =>
        quoteVolume24h >= MajorQuoteVolume ? LiquidityBucket.Major
        : quoteVolume24h >= MidQuoteVolume ? LiquidityBucket.Mid
        : LiquidityBucket.Small;

    /// <summary>Typical half spread when no book sample is available.</summary>
    public static decimal DefaultHalfSpreadPercent(LiquidityBucket bucket) => bucket switch
    {
        LiquidityBucket.Major => 0.005m,
        LiquidityBucket.Mid => 0.02m,
        _ => 0.05m
    };

    public static decimal HalfSpreadPercent(decimal bid, decimal ask)
    {
        if (bid <= 0m || ask <= bid)
        {
            return 0m;
        }

        var mid = (bid + ask) / 2m;
        return (ask - bid) / 2m / mid * 100m;
    }

    /// <summary>
    /// Square-root impact on daily terms: daily volatility % × sqrt(order notional / daily quote volume), capped.
    /// Per-bar volume must not be used here: it inflates impact by sqrt(bars per day).
    /// </summary>
    public static decimal ImpactPercent(decimal orderNotional, decimal dailyQuoteVolume, decimal dailyVolatilityPercent)
    {
        if (orderNotional <= 0m)
        {
            return 0m;
        }

        if (dailyQuoteVolume <= 0m || dailyVolatilityPercent <= 0m)
        {
            return MaxImpactPercent;
        }

        var participation = (double)(orderNotional / dailyQuoteVolume);
        return Math.Min(MaxImpactPercent, dailyVolatilityPercent * (decimal)Math.Sqrt(participation));
    }

    public static CostProfile For(
        CostProfileKind kind,
        LiquidityBucket bucket,
        decimal? sampledHalfSpreadPercent = null,
        decimal impactPercent = 0m)
    {
        var halfSpread = Math.Max(sampledHalfSpreadPercent ?? 0m, DefaultHalfSpreadPercent(bucket));
        var impact = Math.Max(0m, impactPercent);
        return kind switch
        {
            CostProfileKind.Base => new CostProfile(kind, bucket, TakerFeePercent, halfSpread + impact, 0, true),
            CostProfileKind.Conservative => new CostProfile(kind, bucket, TakerFeePercent, 2m * (halfSpread + impact) + 0.01m, 0, true),
            _ => new CostProfile(kind, bucket, StressFeePercent, 3m * (halfSpread + impact) + 0.02m, 1, true)
        };
    }

    public static ReplaySettings Apply(ReplaySettings settings, CostProfile profile) =>
        settings with
        {
            FeePercent = profile.FeePercent,
            SlippagePercent = profile.SlippagePercent,
            ExecutionDelayBars = profile.ExecutionDelayBars
        };
}
