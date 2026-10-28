using System.Globalization;
using System.Text;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Fresh forward rejection screen for one explicitly post-hoc hypothesis discovered by the Phase 4 audit.
/// It is intentionally labelled exploratory: a pass cannot validate it, while a failure rejects it.
/// </summary>
internal static class FundingBasisLongForwardHoldout
{
    private static readonly string[] Symbols =
    [
        "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT",
        "DOGEUSDT", "ADAUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT"
    ];

    private static readonly (string Label, decimal Multiplier)[] Costs =
    [
        ("BASE", 1m), ("MILD", 1.25m), ("HIGH", 1.5m), ("STRESS", 2m)
    ];

    public static async Task<int> RunAsync(string root, CancellationToken cancellationToken)
    {
        var forwardFrom = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
        var forwardTo = new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
        var warmupFrom = forwardFrom.AddDays(-6);
        var cacheRoot = Path.Combine(root, "artifacts", "strategy-validation-cache");
        var futuresRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts"));
        var reportPath = Path.Combine(root, "artifacts", "research", "audit", "funding-basis-long-forward-holdout-2026-10.md");
        var rows = new List<Row>();
        var books = Costs.ToDictionary(cost => cost.Label, _ => new List<ProductionNewBookTrade>(), StringComparer.Ordinal);
        var coverage = new List<string>();

        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformForwardResearch/1.0");
        var futuresClient = new BinanceFuturesHistoryClient(http);

        foreach (var symbol in Symbols)
        {
            var (loaded, _, downloaded) = await ResearchKlineCache.LoadAsync(
                http, cacheRoot, symbol, "1h", warmupFrom, forwardTo, cancellationToken, strictCoverage: true);
            var candles = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
            var funding = await FuturesHistoryCache.LoadOrFetchFundingAsync(futuresClient, futuresRoot, symbol, warmupFrom, forwardTo, false, cancellationToken);
            var mark = await FuturesHistoryCache.LoadOrFetchMarkAsync(futuresClient, futuresRoot, symbol, "1h", warmupFrom, forwardTo, false, cancellationToken);
            var index = await FuturesHistoryCache.LoadOrFetchIndexAsync(futuresClient, futuresRoot, symbol, "1h", warmupFrom, forwardTo, false, cancellationToken);
            var basis = await FuturesHistoryCache.WriteBasisAsync(futuresRoot, symbol, "1h", mark, index, cancellationToken);
            coverage.Add($"{symbol}: bars={candles.Count}, cacheDownload={downloaded}, funding={funding.Count}, mark={mark.Count}, index={index.Count}, basis={basis.Count}");

            var definition = StrategyValidation.Definition(StrategyTemplateKeys.FundingBasisRv, "1h", StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingBasisRv, false) with
            {
                TemplateKey = StrategyTemplateKeys.FundingBasisRv,
                AllowedSide = StrategySides.Long,
                Timeframe = "1h",
                UseFuturesFilter = true,
                ZScoreEntry = 2m,
                FundingLookback = 72,
                EntryLookback = 40,
                FundingHypothesis = "continuation",
                OiHypothesis = "continuation"
            });
            definition.Symbol = symbol;
            var start = candles.FindIndex(c => c.OpenTime >= forwardFrom);
            var warmup = StrategyValidation.WarmupBars(definition);
            if (start < warmup || candles.Count - start < StrategyValidation.MinimumEvaluatedBars)
            {
                coverage.Add($"{symbol}: INSUFFICIENT_DATA for forward window.");
                continue;
            }

            var aligned = FuturesHistoryCache.Align(candles, funding, oi: null, mark, index, basis);
            if (aligned.FundingRate is null || aligned.FundingRate.All(value => value is null)
                || aligned.NormalizedBasis is null || aligned.NormalizedBasis.All(value => value is null))
            {
                coverage.Add($"{symbol}: DATA_UNAVAILABLE (funding or basis alignment).");
                continue;
            }

            var settlements = funding.Select(point => new ReplayFundingSettlement(point.FundingTime, point.FundingRate)).ToList();
            foreach (var (label, multiplier) in Costs)
            {
                var settings = StrategyValidation.FrozenRisk(forwardFrom, forwardTo) with
                {
                    FeePercent = 0.04m * multiplier,
                    SlippagePercent = 0.05m * multiplier,
                    HonorSuggestedStops = true,
                    PreserveNullTake = true,
                    KeepSuggestedTarget = true
                };
                var replay = new BacktestReplay(new StrategyEngine()).Run(
                    definition, candles, settings, new CausalIndicatorCache(candles), start, candles.Count, null, aligned, settlements, symbol);
                rows.Add(new Row(label, symbol, replay.NumberOfTrades, replay.NetProfit, replay.ProfitFactor, replay.MaximumDrawdown, replay.FeesPaid, replay.SlippagePaid, replay.FundingPaid));
                books[label].AddRange(replay.Trades.Select(trade => new ProductionNewBookTrade(
                    "funding_basis_rv_long_forward", symbol, trade.Side, trade.OpenedAt, trade.ClosedAt,
                    trade.PnL, trade.GrossPnl, trade.Fees, trade.SlippageCost, trade.FundingPnl, settings.InitialBalance)));
            }
        }

        var overlays = books.ToDictionary(
            pair => pair.Key,
            pair => ProductionNewPortfolio.Simulate(
                pair.Value,
                initialEquity: 10_000m,
                riskPercent: 1m,
                maxAggregateRiskPercent: 4m,
                dailyLossPercent: 5m,
                maxPositions: 2),
            StringComparer.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, Render(forwardFrom, forwardTo, rows, overlays, coverage));
        Console.WriteLine($"wrote {reportPath}");
        return 0;
    }

    private static string Render(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<Row> rows,
        IReadOnlyDictionary<string, ProductionNewPortfolio.Result> overlays,
        IReadOnlyList<string> coverage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Funding/Basis long-only forward holdout");
        sb.AppendLine();
        sb.AppendLine("## Status");
        sb.AppendLine();
        sb.AppendLine("EXPLORATORY_FORWARD_SCREEN. The long-only rule was proposed after Phase 4 observed directional asymmetry, so this report can reject it but cannot validate or promote it. The tested interval begins after Phase 4's 2026-09-19 data cutoff.");
        sb.AppendLine();
        sb.AppendLine("## Frozen rule");
        sb.AppendLine();
        sb.AppendLine("`funding_basis_rv`, 1h, long only, funding and normalized-basis co-extreme z-score >= 2, funding lookback 72 bars, basis lookback 40 bars, next-bar fills, settled funding, 1% risk and 5x maximum leverage in the existing FrozenRisk book.");
        sb.AppendLine();
        sb.AppendLine($"Forward interval: {from:yyyy-MM-dd} through {to:yyyy-MM-dd} UTC. No parameters were tuned on this interval.");
        sb.AppendLine();
        sb.AppendLine("## Results");
        sb.AppendLine();
        sb.AppendLine("The PF and isolated net are diagnostic only. Shared-account results apply the frozen two-position risk overlay and are the relevant risk result.");
        sb.AppendLine();
        sb.AppendLine("| Cost | Raw trades | Raw isolated PF | Shared net PnL | Shared max drawdown | Accepted / rejected |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var group in rows.GroupBy(row => row.Cost).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var total = group.ToList();
            var wins = total.Where(row => row.Net > 0m).Sum(row => row.Net);
            var losses = -total.Where(row => row.Net < 0m).Sum(row => row.Net);
            var pf = losses == 0m ? "N/A" : (wins / losses).ToString("0.000", CultureInfo.InvariantCulture);
            var overlay = overlays[group.Key];
            sb.AppendLine($"| {group.Key} | {total.Sum(row => row.Trades)} | {pf} | {overlay.NetPnl:0.00} | {overlay.MaxDrawdownPercent:0.00}% | {overlay.Accepted} / {overlay.Rejected} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Interpretation gate");
        sb.AppendLine();
        sb.AppendLine("A negative BASE shared-account result rejects this rule. A positive result remains insufficient because the long-only direction was selected after earlier analysis and the screen has too few trades; it would require a new independent forward sample before any paper consideration.");
        sb.AppendLine();
        sb.AppendLine("## Data coverage");
        foreach (var row in coverage)
        {
            sb.AppendLine($"- {row}");
        }
        return sb.ToString();
    }

    private sealed record Row(string Cost, string Symbol, int Trades, decimal Net, decimal ProfitFactor, decimal Drawdown, decimal Fees, decimal Slippage, decimal Funding);
}
