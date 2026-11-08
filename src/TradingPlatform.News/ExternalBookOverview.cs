using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.News;
using TradingPlatform.Domain.Risk;

namespace TradingPlatform.News;

/// <summary>News and pump shorts are not bots. The risk overview still lists them as their own rows.</summary>
public static class ExternalBookOverview
{
    public const string NewsName = "News";
    public const string PumpName = "Pump shorts";

    public static StrategyResultDto Pump(UnexplainedPumpDeskFile desk)
    {
        var rows = desk.Positions ?? [];
        var closed = rows.Where(row => !row.Open && row.Pnl is not null).ToList();
        var open = rows.Count(row => row.Open);
        return new StrategyResultDto(
            PumpName,
            rows.Count,
            closed.Count(row => row.Pnl > 0m),
            closed.Count(row => row.Pnl < 0m),
            open,
            Round(closed.Sum(row => row.Pnl ?? 0m)),
            Round(rows.Where(row => row.Open && row.PnlKnown).Sum(row => row.Pnl ?? 0m)),
            UnexplainedPumpShort.MaxStopPercent,
            null,
            null,
            true);
    }

    public static StrategyResultDto News(IReadOnlyList<NewsTradeExecution> executions)
    {
        var filled = executions.Where(row => row.OrderFilledAtUtc is not null).ToList();
        var closed = filled.Where(row => row.ClosedAtUtc is not null && row.RealizedPnL is not null).ToList();
        return new StrategyResultDto(
            NewsName,
            filled.Count,
            closed.Count(row => row.RealizedPnL > 0m),
            closed.Count(row => row.RealizedPnL < 0m),
            filled.Count(row => row.ClosedAtUtc is null),
            Round(closed.Sum(row => row.RealizedPnL ?? 0m)),
            0m,
            NewsRiskBook.StopLossPercent,
            NewsRiskBook.TakeProfitPercent,
            NewsRiskBook.MaxSimultaneousPositions,
            true);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
