using System.Text.Json;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.News;

namespace TradingPlatform.News;

/// <summary>
/// Closed pump shorts and news trades leave the bot ledger. The account day, month, and today
/// figures still add their realized cash on the close date.
/// </summary>
public static class ExternalBookCash
{
    public readonly record struct DatedCash(DateTimeOffset At, decimal Amount);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<DatedCash> Load(string root, IReadOnlyList<NewsTradeExecution> news)
    {
        var rows = new List<DatedCash>();
        rows.AddRange(Pump(root));
        foreach (var trade in news)
        {
            if (trade.ClosedAtUtc is { } closed && trade.RealizedPnL is { } pnl)
            {
                rows.Add(new DatedCash(closed, pnl));
            }
        }

        return rows;
    }

    public static decimal Sum(IEnumerable<DatedCash> cash, DateTimeOffset fromInclusive, DateTimeOffset toExclusive) =>
        cash.Where(row => row.At >= fromInclusive && row.At < toExclusive).Sum(row => row.Amount);

    public static IReadOnlyList<PerformanceDayDto> Add(IReadOnlyList<PerformanceDayDto> days, IEnumerable<DatedCash> cash)
    {
        var amounts = days.ToDictionary(day => day.Date, day => day.PnL);
        foreach (var row in cash)
        {
            var key = row.At.UtcDateTime.ToString("yyyy-MM-dd");
            if (amounts.ContainsKey(key))
            {
                amounts[key] += row.Amount;
            }
        }

        decimal cumulative = 0m;
        return days.Select(day =>
        {
            var pnl = Round(amounts[day.Date]);
            cumulative = Round(cumulative + pnl);
            return new PerformanceDayDto(day.Date, pnl, cumulative);
        }).ToList();
    }

    public static PerformanceDto Apply(PerformanceDto performance, IReadOnlyList<DatedCash> cash, DateTimeOffset now)
    {
        if (cash.Count == 0)
        {
            return performance;
        }

        var today = UtcDate(now);
        var month = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return performance with
        {
            Days = Add(performance.Days, cash),
            TodaysPnL = Round(performance.TodaysPnL + Sum(cash, today, today.AddDays(1))),
            MonthlyPnL = Round(performance.MonthlyPnL + Sum(cash, month, month.AddMonths(1))),
            RealizedPnL = Round(performance.RealizedPnL + cash.Sum(row => row.Amount))
        };
    }

    public static PortfolioDto Apply(PortfolioDto overview, IReadOnlyList<DatedCash> cash, DateTimeOffset now)
    {
        var today = UtcDate(now);
        var extra = Sum(cash, today, today.AddDays(1));
        return extra == 0m ? overview : overview with { LiveTodaysPnL = Round(overview.LiveTodaysPnL + extra) };
    }

    private static IEnumerable<DatedCash> Pump(string root)
    {
        var path = Path.Combine(root, "artifacts", "data", "pump-shorts", "state.json");
        if (!File.Exists(path))
        {
            yield break;
        }

        PumpStateFile? book;
        try
        {
            book = JsonSerializer.Deserialize<PumpStateFile>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            yield break;
        }

        foreach (var ticket in book?.Tickets ?? [])
        {
            if (ticket.Closed && ticket.ClosedAt is { } closed && ticket.RealizedPnl is { } pnl)
            {
                yield return new DatedCash(closed, pnl);
            }
        }
    }

    private static DateTimeOffset UtcDate(DateTimeOffset now) =>
        new(now.UtcDateTime.Date, TimeSpan.Zero);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed class PumpStateFile
    {
        public List<PumpStateTicket> Tickets { get; set; } = [];
    }

    private sealed class PumpStateTicket
    {
        public bool Closed { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public decimal? RealizedPnl { get; set; }
    }
}
