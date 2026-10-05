using System.Text.Json;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Execution.Shadow;

public enum ShadowFillKind
{
    Trigger = 0,
    Funding = 1
}

/// <summary>
/// A fill or funding payment the shadow exchange made on its own (a stop, a take, or a funding settlement).
/// The engine did not place it, so it has to be booked into the trading store the way the Binance history sync books real ones.
/// </summary>
public sealed record ShadowFill(
    string Id,
    ShadowFillKind Kind,
    string Symbol,
    string ClientOrderId,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    decimal RealizedPnl,
    decimal Funding,
    DateTimeOffset At,
    string Reason);

public sealed record ShadowAccount(decimal Wallet, decimal Unrealized, decimal Equity, decimal UsedMargin, decimal Available);

/// <summary>
/// A one-way-mode USD-M account simulated on real Binance prices. Market orders fill at the touch plus modeled
/// slippage; closePosition stops and takes trigger on mark price; funding settles from real rates every 8 hours.
/// Liquidation is not simulated: the risk engine keeps every stop inside the liquidation price.
/// </summary>
public sealed class ShadowExchange
{
    public const string FeeAsset = "USDT";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly ShadowExchangeOptions _options;
    private readonly ShadowState _state;

    public ShadowExchange(ShadowExchangeOptions options)
    {
        _options = options;
        _state = Load(options) ?? new ShadowState { Wallet = options.StartingBalance };
    }

    public ShadowExchangeOptions Options => _options;

    public decimal SlippageFraction => Math.Max(0m, _options.SlippageBps) / 10_000m;

    public decimal FeeFraction => Math.Max(0m, _options.TakerFeePercent) / 100m;

    public ExchangeOrder PlaceMarket(PlaceOrderRequest request, FuturesBookTicker? book, IReadOnlyDictionary<string, decimal> marks, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_state.Orders.TryGetValue(request.ClientOrderId, out var known))
            {
                return known.ToExchange();
            }

            var symbol = request.Symbol.ToUpperInvariant();
            if (request.Type != OrderType.Market)
            {
                return Reject(request, symbol, now, "Shadow fills market orders only.");
            }

            if (request.Quantity <= 0m)
            {
                return Reject(request, symbol, now, "Quantity must be positive.");
            }

            if (book is null || book.Bid <= 0m || book.Ask <= 0m)
            {
                throw new InvalidOperationException($"No Binance book price for {symbol}. The shadow order was not accepted.");
            }

            var direction = request.Side == OrderSide.Buy ? 1m : -1m;
            var touch = request.Side == OrderSide.Buy ? book.Ask : book.Bid;
            var fill = touch * (1m + direction * SlippageFraction);
            _state.Positions.TryGetValue(symbol, out var position);
            var held = position?.Quantity ?? 0m;
            var quantity = request.Quantity;
            if (request.ReduceOnly)
            {
                if (held == 0m || Math.Sign(held) == Math.Sign(direction))
                {
                    return Reject(request, symbol, now, "ReduceOnly Order is rejected.");
                }

                quantity = Math.Min(quantity, Math.Abs(held));
            }

            var closing = held != 0m && Math.Sign(held) != Math.Sign(direction) ? Math.Min(quantity, Math.Abs(held)) : 0m;
            var opening = quantity - closing;
            var leverage = Leverage(symbol);
            if (opening > 0m)
            {
                var need = opening * fill / leverage;
                if (need > AccountLocked(marks).Available)
                {
                    return Reject(request, symbol, now, "Margin is insufficient.");
                }
            }

            var realized = closing > 0m ? closing * (fill - position!.EntryPrice) * Math.Sign(held) : 0m;
            var fee = quantity * fill * FeeFraction;
            _state.Wallet += realized - fee;
            Apply(symbol, position, held, direction, closing, opening, fill, leverage, now);

            var order = new ShadowOrder
            {
                ClientOrderId = request.ClientOrderId,
                ExchangeOrderId = NextId(),
                Symbol = symbol,
                Side = request.Side,
                Type = OrderType.Market,
                Status = OrderStatus.Filled,
                Quantity = quantity,
                Filled = quantity,
                AveragePrice = fill,
                Fee = fee,
                At = now
            };
            _state.Orders[order.ClientOrderId] = order;
            Save();
            return order.ToExchange();
        }
    }

    public ProtectiveStopsResult PlaceStops(
        string symbol,
        OrderSide closeSide,
        decimal stopPrice,
        decimal takePrice,
        string stopClientOrderId,
        string takeClientOrderId,
        decimal? mark,
        bool placeStop,
        bool placeTake,
        bool acceptExisting,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            symbol = symbol.ToUpperInvariant();
            string? stopError = null;
            string? takeError = null;
            var stopPlaced = placeStop && Arm(symbol, closeSide, stopPrice, stopClientOrderId, isStop: true, mark, acceptExisting, now, out stopError);
            var takePlaced = placeTake && Arm(symbol, closeSide, takePrice, takeClientOrderId, isStop: false, mark, acceptExisting, now, out takeError);
            Save();
            return new ProtectiveStopsResult(stopPlaced, takePlaced, stopError, takeError);
        }
    }

    public void Cancel(string? clientOrderId, string? exchangeOrderId)
    {
        lock (_gate)
        {
            var algo = _state.Algos.Values.FirstOrDefault(row =>
                (!string.IsNullOrWhiteSpace(clientOrderId) && row.ClientOrderId == clientOrderId)
                || (!string.IsNullOrWhiteSpace(exchangeOrderId) && row.ExchangeOrderId == exchangeOrderId));
            if (algo is { Status: OrderStatus.Submitted })
            {
                algo.Status = OrderStatus.Cancelled;
                Save();
            }
        }
    }

    public void CancelAll(string symbol)
    {
        lock (_gate)
        {
            foreach (var algo in Working(symbol.ToUpperInvariant()))
            {
                algo.Status = OrderStatus.Cancelled;
            }

            Save();
        }
    }

    public OrderLookup Lookup(string? clientOrderId, string? exchangeOrderId)
    {
        lock (_gate)
        {
            var order = _state.Orders.Values.FirstOrDefault(row =>
                (!string.IsNullOrWhiteSpace(clientOrderId) && row.ClientOrderId == clientOrderId)
                || (!string.IsNullOrWhiteSpace(exchangeOrderId) && row.ExchangeOrderId == exchangeOrderId));
            if (order is not null)
            {
                return OrderLookup.Found(order.ToExchange());
            }

            var algo = _state.Algos.Values.FirstOrDefault(row =>
                (!string.IsNullOrWhiteSpace(clientOrderId) && row.ClientOrderId == clientOrderId)
                || (!string.IsNullOrWhiteSpace(exchangeOrderId) && row.ExchangeOrderId == exchangeOrderId));
            return algo is null
                ? OrderLookup.Absent("The shadow exchange never accepted this order.")
                : OrderLookup.Found(algo.ToExchange());
        }
    }

    public IReadOnlyList<ExchangeOrder> OpenOrders(string? symbol)
    {
        lock (_gate)
        {
            return _state.Algos.Values
                .Where(row => row.Status == OrderStatus.Submitted
                    && (symbol is null || string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
                .Select(row => row.ToExchange())
                .ToList();
        }
    }

    public IReadOnlyList<ShadowAlgo> WorkingAlgos()
    {
        lock (_gate)
        {
            return _state.Algos.Values.Where(row => row.Status == OrderStatus.Submitted).Select(row => row.Copy()).ToList();
        }
    }

    public IReadOnlyList<ExchangePosition> Positions(IReadOnlyDictionary<string, decimal> marks)
    {
        lock (_gate)
        {
            return _state.Positions.Values
                .Where(row => row.Quantity != 0m)
                .Select(row => new ExchangePosition(
                    row.Symbol,
                    row.Quantity > 0m ? PositionSide.Long : PositionSide.Short,
                    Math.Abs(row.Quantity),
                    row.EntryPrice,
                    MarkFor(row, marks)))
                .ToList();
        }
    }

    public IReadOnlyList<string> Symbols()
    {
        lock (_gate)
        {
            return _state.Positions.Keys
                .Concat(_state.Algos.Values.Where(row => row.Status == OrderStatus.Submitted).Select(row => row.Symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public ShadowAccount Account(IReadOnlyDictionary<string, decimal> marks)
    {
        lock (_gate)
        {
            return AccountLocked(marks);
        }
    }

    public void SetLeverage(string symbol, int leverage)
    {
        lock (_gate)
        {
            _state.Leverage[symbol.ToUpperInvariant()] = Math.Clamp(leverage, 1, Math.Max(1, _options.MaxLeverage));
            Save();
        }
    }

    /// <summary>Triggers stops and takes on mark price and settles funding. Returns how many fills happened.</summary>
    public int Tick(
        IReadOnlyDictionary<string, decimal> marks,
        IReadOnlyDictionary<string, decimal> fundingRates,
        Func<string, FuturesBookTicker?> book,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            var events = 0;
            foreach (var position in _state.Positions.Values.ToList())
            {
                if (marks.TryGetValue(position.Symbol, out var mark) && mark > 0m)
                {
                    position.Mark = mark;
                }
            }

            foreach (var algo in _state.Algos.Values.Where(row => row.Status == OrderStatus.Submitted).ToList())
            {
                if (!marks.TryGetValue(algo.Symbol, out var mark) || mark <= 0m)
                {
                    continue;
                }

                _state.Positions.TryGetValue(algo.Symbol, out var position);
                var closesLong = algo.CloseSide == OrderSide.Sell;
                if (position is null || position.Quantity == 0m || (position.Quantity > 0m) != closesLong)
                {
                    continue;
                }

                if (!Triggered(algo, mark))
                {
                    continue;
                }

                var touch = book(algo.Symbol);
                var raw = touch is null ? mark : closesLong ? touch.Bid : touch.Ask;
                var fill = raw * (1m + (closesLong ? -1m : 1m) * SlippageFraction);
                var quantity = Math.Abs(position.Quantity);
                var realized = quantity * (fill - position.EntryPrice) * Math.Sign(position.Quantity);
                var fee = quantity * fill * FeeFraction;
                _state.Wallet += realized - fee;
                _state.Positions.Remove(algo.Symbol);
                algo.Status = OrderStatus.Filled;
                algo.Filled = quantity;
                algo.AveragePrice = fill;
                algo.Fee = fee;
                foreach (var sibling in Working(algo.Symbol))
                {
                    sibling.Status = OrderStatus.Cancelled;
                }

                _state.Unbooked.Add(new ShadowFill(
                    "S" + NextId(),
                    ShadowFillKind.Trigger,
                    algo.Symbol,
                    algo.ClientOrderId,
                    algo.CloseSide,
                    quantity,
                    fill,
                    fee,
                    realized,
                    0m,
                    now,
                    algo.IsStop ? "Stop loss" : "Take profit"));
                events++;
            }

            foreach (var position in _state.Positions.Values.ToList())
            {
                var settlement = LastSettlement(now);
                if (position.LastFundingAt >= settlement || position.OpenedAt >= settlement)
                {
                    continue;
                }

                position.LastFundingAt = settlement;
                if (!fundingRates.TryGetValue(position.Symbol, out var rate) || rate == 0m)
                {
                    continue;
                }

                var payment = -Math.Sign(position.Quantity) * Math.Abs(position.Quantity) * MarkFor(position, marks) * rate;
                _state.Wallet += payment;
                _state.Unbooked.Add(new ShadowFill(
                    "F" + NextId(),
                    ShadowFillKind.Funding,
                    position.Symbol,
                    "",
                    OrderSide.Buy,
                    0m,
                    0m,
                    0m,
                    0m,
                    payment,
                    settlement,
                    "Funding"));
                events++;
            }

            if (events > 0 || _state.Positions.Count > 0)
            {
                Save();
            }

            return events;
        }
    }

    public IReadOnlyList<ShadowFill> PendingFills()
    {
        lock (_gate)
        {
            return _state.Unbooked.ToList();
        }
    }

    public void Acknowledge(IEnumerable<string> fillIds)
    {
        lock (_gate)
        {
            var done = fillIds.ToHashSet(StringComparer.Ordinal);
            if (_state.Unbooked.RemoveAll(fill => done.Contains(fill.Id)) > 0)
            {
                Save();
            }
        }
    }

    /// <summary>Most recent 00:00, 08:00 or 16:00 UTC at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset LastSettlement(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour / 8 * 8, 0, 0, TimeSpan.Zero);
    }

    public static bool Triggered(ShadowAlgo algo, decimal mark)
    {
        var closesLong = algo.CloseSide == OrderSide.Sell;
        return algo.IsStop
            ? closesLong ? mark <= algo.Trigger : mark >= algo.Trigger
            : closesLong ? mark >= algo.Trigger : mark <= algo.Trigger;
    }

    private bool Arm(
        string symbol,
        OrderSide closeSide,
        decimal trigger,
        string clientOrderId,
        bool isStop,
        decimal? mark,
        bool acceptExisting,
        DateTimeOffset now,
        out string? error)
    {
        error = null;
        if (trigger <= 0m || string.IsNullOrWhiteSpace(clientOrderId))
        {
            error = "Trigger price and client id are required.";
            return false;
        }

        if (_state.Algos.TryGetValue(clientOrderId, out var existing) && existing.Status == OrderStatus.Submitted)
        {
            if (acceptExisting)
            {
                return true;
            }

            error = "-4116 ClientOrderId is duplicated.";
            return false;
        }

        var algo = new ShadowAlgo
        {
            ClientOrderId = clientOrderId,
            ExchangeOrderId = NextId(),
            Symbol = symbol,
            CloseSide = closeSide,
            IsStop = isStop,
            Trigger = trigger,
            Status = OrderStatus.Submitted,
            At = now
        };
        if (mark is > 0m && Triggered(algo, mark.Value))
        {
            error = "-2021 Order would immediately trigger.";
            return false;
        }

        _state.Algos[clientOrderId] = algo;
        return true;
    }

    private void Apply(string symbol, ShadowPosition? position, decimal held, decimal direction, decimal closing, decimal opening, decimal fill, int leverage, DateTimeOffset now)
    {
        var remaining = Math.Abs(held) - closing;
        if (position is not null && closing > 0m)
        {
            if (remaining <= 0m)
            {
                _state.Positions.Remove(symbol);
                position = null;
            }
            else
            {
                position.Quantity = Math.Sign(held) * remaining;
            }
        }

        if (opening <= 0m)
        {
            return;
        }

        if (position is null)
        {
            _state.Positions[symbol] = new ShadowPosition
            {
                Symbol = symbol,
                Quantity = direction * opening,
                EntryPrice = fill,
                Leverage = leverage,
                OpenedAt = now,
                LastFundingAt = now,
                Mark = fill
            };
            return;
        }

        var total = Math.Abs(position.Quantity) + opening;
        position.EntryPrice = (position.EntryPrice * Math.Abs(position.Quantity) + fill * opening) / total;
        position.Quantity = direction * total;
    }

    private ShadowAccount AccountLocked(IReadOnlyDictionary<string, decimal> marks)
    {
        var unrealized = 0m;
        var margin = 0m;
        foreach (var position in _state.Positions.Values)
        {
            unrealized += position.Quantity * (MarkFor(position, marks) - position.EntryPrice);
            margin += Math.Abs(position.Quantity) * position.EntryPrice / Math.Max(1, position.Leverage);
        }

        var equity = _state.Wallet + unrealized;
        return new ShadowAccount(_state.Wallet, unrealized, equity, margin, Math.Max(0m, equity - margin));
    }

    private static decimal MarkFor(ShadowPosition position, IReadOnlyDictionary<string, decimal> marks) =>
        marks.TryGetValue(position.Symbol, out var mark) && mark > 0m ? mark : position.Mark > 0m ? position.Mark : position.EntryPrice;

    private int Leverage(string symbol) =>
        _state.Leverage.TryGetValue(symbol, out var leverage) && leverage > 0 ? leverage : 1;

    private IEnumerable<ShadowAlgo> Working(string symbol) =>
        _state.Algos.Values.Where(row => row.Status == OrderStatus.Submitted && row.Symbol == symbol).ToList();

    private ExchangeOrder Reject(PlaceOrderRequest request, string symbol, DateTimeOffset now, string reason)
    {
        var order = new ShadowOrder
        {
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = NextId(),
            Symbol = symbol,
            Side = request.Side,
            Type = request.Type,
            Status = OrderStatus.Rejected,
            Quantity = request.Quantity,
            At = now,
            Reason = reason
        };
        _state.Orders[order.ClientOrderId] = order;
        Save();
        return order.ToExchange();
    }

    private string NextId() => (++_state.NextId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_options.StatePath))
        {
            return;
        }

        var path = Path.GetFullPath(_options.StatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_state, Json));
        File.Move(temp, path, overwrite: true);
    }

    private static ShadowState? Load(ShadowExchangeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StatePath))
        {
            return null;
        }

        var path = Path.GetFullPath(options.StatePath);
        return File.Exists(path) ? JsonSerializer.Deserialize<ShadowState>(File.ReadAllText(path), Json) : null;
    }
}

public sealed class ShadowState
{
    public decimal Wallet { get; set; }
    public long NextId { get; set; }
    public Dictionary<string, ShadowPosition> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ShadowOrder> Orders { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ShadowAlgo> Algos { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Leverage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ShadowFill> Unbooked { get; set; } = [];
}

public sealed class ShadowPosition
{
    public string Symbol { get; set; } = "";
    /// <summary>Signed: positive is long.</summary>
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public int Leverage { get; set; } = 1;
    public decimal Mark { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset LastFundingAt { get; set; }
}

public sealed class ShadowOrder
{
    public string ClientOrderId { get; set; } = "";
    public string ExchangeOrderId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Quantity { get; set; }
    public decimal Filled { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal Fee { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Reason { get; set; }

    public ExchangeOrder ToExchange() => new(
        ClientOrderId,
        ExchangeOrderId,
        Symbol,
        Side,
        Type,
        Status,
        Quantity,
        Filled,
        AveragePrice,
        AveragePrice,
        At,
        Fee,
        FeeKnown: Status == OrderStatus.Filled,
        CumulativeQuote: AveragePrice is { } price ? price * Filled : null,
        FeeAsset: Status == OrderStatus.Filled ? ShadowExchange.FeeAsset : null);
}

public sealed class ShadowAlgo
{
    public string ClientOrderId { get; set; } = "";
    public string ExchangeOrderId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public OrderSide CloseSide { get; set; }
    public bool IsStop { get; set; }
    public decimal Trigger { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Filled { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal Fee { get; set; }
    public DateTimeOffset At { get; set; }

    public ShadowAlgo Copy() => (ShadowAlgo)MemberwiseClone();

    public ExchangeOrder ToExchange() => new(
        ClientOrderId,
        ExchangeOrderId,
        Symbol,
        CloseSide,
        IsStop ? OrderType.StopMarket : OrderType.TakeProfitMarket,
        Status,
        Filled,
        Filled,
        Trigger,
        AveragePrice,
        At,
        Fee,
        FeeKnown: Status == OrderStatus.Filled,
        CumulativeQuote: AveragePrice is { } price ? price * Filled : null,
        FeeAsset: Status == OrderStatus.Filled ? ShadowExchange.FeeAsset : null);
}
