namespace TradingPlatform.Research.Alpha;

/// <summary>A causal per-coin value at the close of a bar; NaN when undefined.</summary>
public delegate double CoinSignal(int coin, int hour);

/// <summary>Optional book-level switch evaluated at the close of a decision bar (regime conditioner).</summary>
public delegate bool HourGate(int hour);

public enum LegMode
{
    LongShort,
    LongOnly,
    ShortOnly
}

/// <summary>
/// Cross-sectional quantile book: at every <c>step</c>-hour close, rank eligible coins by the signal, go long the top
/// quantile and short the bottom quantile, equal weight, dollar neutral, gross 1. Holding <c>hold</c> hours is done
/// with hold/step overlapping sleeves whose targets are averaged.
/// </summary>
public sealed class CrossSectionalModel : ITargetModel
{
    public const int MinimumCoins = 20;
    private readonly PanelUniverse _universe;
    private readonly CoinSignal _signal;
    private readonly int _step;
    private readonly double _quantile;
    private readonly LegMode _mode;
    private readonly HourGate? _gate;
    private readonly Queue<double[]> _sleeves = new();
    private readonly int _sleeveCount;

    public CrossSectionalModel(PanelUniverse universe, CoinSignal signal, int step, int hold, double quantile, LegMode mode = LegMode.LongShort, HourGate? gate = null)
    {
        _universe = universe;
        _signal = signal;
        _step = Math.Max(1, step);
        _quantile = quantile;
        _mode = mode;
        _gate = gate;
        _sleeveCount = Math.Max(1, hold / _step);
    }

    public bool IsDecision(int hour) => (hour + 1) % _step == 0;

    public void Targets(int hour, double[] target)
    {
        var sleeve = new double[target.Length];
        if (_gate is null || _gate(hour))
        {
            Rank(hour, sleeve);
        }

        _sleeves.Enqueue(sleeve);
        while (_sleeves.Count > _sleeveCount)
        {
            _sleeves.Dequeue();
        }

        foreach (var s in _sleeves)
        {
            for (var c = 0; c < target.Length; c++)
            {
                target[c] += s[c] / _sleeveCount;
            }
        }
    }

    private void Rank(int hour, double[] sleeve)
    {
        var rows = new List<(int Coin, double Value)>();
        for (var c = 0; c < _universe.Panel.Coins; c++)
        {
            if (!_universe.Eligible(c, hour))
            {
                continue;
            }

            var v = _signal(c, hour);
            if (double.IsFinite(v))
            {
                rows.Add((c, v));
            }
        }

        if (rows.Count < MinimumCoins)
        {
            return;
        }

        rows.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : a.Coin.CompareTo(b.Coin));
        var k = Math.Max(1, (int)Math.Floor(rows.Count * _quantile));
        var longSide = _mode == LegMode.ShortOnly ? 0d : _mode == LegMode.LongOnly ? 1d : 0.5d;
        var shortSide = _mode == LegMode.LongOnly ? 0d : _mode == LegMode.ShortOnly ? 1d : 0.5d;
        for (var i = 0; i < k; i++)
        {
            sleeve[rows[^(i + 1)].Coin] += longSide / k;
            sleeve[rows[i].Coin] -= shortSide / k;
        }
    }
}

/// <summary>An entry for one coin: direction +1 / −1, or 0 for none.</summary>
public delegate int EventTrigger(int coin, int hour);

public sealed record EventTrade(int Coin, int EntryHour, int ExitHour, int Direction);

/// <summary>
/// Time-series events: at each <c>step</c>-hour close, every eligible coin without an open event may trigger one,
/// held for <c>hold</c> hours at 1/<c>slots</c> of the book. The simulator caps gross exposure at 1.
/// With <c>hedgeCoin</c> the book's net exposure is offset in that coin (BTC) at the same close.
/// </summary>
public sealed class EventModel : ITargetModel
{
    private readonly PanelUniverse _universe;
    private readonly EventTrigger _trigger;
    private readonly int _step;
    private readonly int _hold;
    private readonly double _unit;
    private readonly int _hedgeCoin;
    private readonly HourGate? _gate;
    private readonly Dictionary<int, (int Entry, int Direction)> _open = new();

    public EventModel(PanelUniverse universe, EventTrigger trigger, int step, int hold, int slots = 20, int hedgeCoin = -1, HourGate? gate = null)
    {
        _universe = universe;
        _trigger = trigger;
        _step = Math.Max(1, step);
        _hold = Math.Max(1, hold);
        _unit = 1d / Math.Max(1, slots);
        _hedgeCoin = hedgeCoin;
        _gate = gate;
    }

    public List<EventTrade> Trades { get; } = [];

    public bool IsDecision(int hour) => (hour + 1) % _step == 0;

    /// <summary>Records events still open when the window ends; the simulator flattens them at its last close.</summary>
    public void Flush(int lastHour)
    {
        foreach (var (coin, (entry, direction)) in _open)
        {
            Trades.Add(new EventTrade(coin, entry, lastHour, direction));
        }

        _open.Clear();
    }

    public void Targets(int hour, double[] target)
    {
        foreach (var coin in _open.Keys.ToList())
        {
            var (entry, direction) = _open[coin];
            if (hour - entry >= _hold || float.IsNaN(_universe.Panel.Close[coin][hour]))
            {
                Trades.Add(new EventTrade(coin, entry, hour, direction));
                _open.Remove(coin);
            }
        }

        if (_gate is null || _gate(hour))
        {
            for (var c = 0; c < _universe.Panel.Coins; c++)
            {
                if (c == _hedgeCoin || _open.ContainsKey(c) || !_universe.Eligible(c, hour))
                {
                    continue;
                }

                var d = _trigger(c, hour);
                if (d != 0)
                {
                    _open[c] = (hour, Math.Sign(d));
                }
            }
        }

        var net = 0d;
        foreach (var (coin, (_, direction)) in _open)
        {
            target[coin] = direction * _unit;
            net += direction * _unit;
        }

        if (_hedgeCoin >= 0 && net != 0d && _universe.Eligible(_hedgeCoin, hour))
        {
            target[_hedgeCoin] = -net;
        }
    }
}
