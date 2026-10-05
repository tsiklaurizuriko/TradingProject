using TradingPlatform.Research.Alpha;

namespace TradingPlatform.StrategyResearch.AlphaDiscovery;

internal sealed class AlphaContext(PanelUniverse universe, PanelFeatures features, AlphaSplits splits, MetricsPanel? metrics = null)
{
    public PanelUniverse U { get; } = universe;
    public PanelFeatures F { get; } = features;
    public HourlyPanel P => U.Panel;
    public AlphaSplits Splits { get; } = splits;
    public MetricsPanel? Metrics { get; } = metrics;

    /// <summary>Same panel with the ≥ $50M median daily quote volume universe (phase 2b). Set by the runner.</summary>
    public AlphaContext? Liquid { get; set; }
}

/// <summary>Cross-sectional rank of a feature among eligible coins at one hour, scaled to [-0.5, 0.5]. Not thread safe; one per model.</summary>
internal sealed class XsRanker(PanelUniverse universe, CoinSignal feature)
{
    private int _hour = -1;
    private double[] _ranks = [];

    public double Rank(int coin, int hour)
    {
        if (hour != _hour)
        {
            Compute(hour);
        }

        return _ranks[coin];
    }

    private void Compute(int hour)
    {
        var n = universe.Panel.Coins;
        var ranks = new double[n];
        Array.Fill(ranks, double.NaN);
        var rows = new List<(int Coin, double Value)>();
        for (var c = 0; c < n; c++)
        {
            if (universe.Eligible(c, hour))
            {
                var v = feature(c, hour);
                if (double.IsFinite(v))
                {
                    rows.Add((c, v));
                }
            }
        }

        rows.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : a.Coin.CompareTo(b.Coin));
        for (var i = 0; i < rows.Count; i++)
        {
            ranks[rows[i].Coin] = rows.Count > 1 ? i / (double)(rows.Count - 1) - 0.5 : 0d;
        }

        _ranks = ranks;
        _hour = hour;
    }
}

internal delegate ITargetModel ModelFactory(AlphaContext ctx, IReadOnlyDictionary<string, double> p);

/// <summary>One grid point. Params are numeric; <c>sign</c> is +1 for the registered direction, −1 for the flipped grid.</summary>
internal sealed record AlphaConfig(string Family, string Variant, string Kind, SortedDictionary<string, double> Params, ModelFactory Factory, string[] Perturb)
{
    public string Id => $"{Family}.{Variant}." + string.Join(".", Params.Select(p => $"{p.Key}{Fmt(p.Value)}"));

    public AlphaConfig With(string key, double value)
    {
        var copy = new SortedDictionary<string, double>(Params, StringComparer.Ordinal) { [key] = value };
        return this with { Params = copy };
    }

    public static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record ScreenSpec(string Name, string Kind, int Horizon, int Step, Func<AlphaContext, CoinSignal>? Signal, Func<AlphaContext, EventTrigger>? Trigger);

internal sealed record FamilySpec(
    string Id,
    string Name,
    string Priority,
    string Timeframe,
    string Mechanism,
    string Hypothesis,
    string Data,
    bool UsesMetrics,
    IReadOnlyList<ScreenSpec> Screens,
    IReadOnlyList<AlphaConfig> Grid,
    bool LiquidUniverse = false);

/// <summary>
/// The pre-registered families. Lookbacks, holds and steps are in hours. Every grid is coarse and capped near 36
/// points. Signs are fixed here; a flipped grid is only added by the runner's pre-registered sign rule.
/// </summary>
internal static class AlphaFamilies
{
    private const int D = 24;
    private const int W = 168;
    private const int M = 720;

    public static IReadOnlyList<FamilySpec> All { get; } = Build();

    /// <summary>
    /// Phase 2b, registered after the phase 2 IS results and before any phase 2b run. Motivation (IS only): several
    /// signals carry IS information but turnover costs exceed gross PnL by 5–20×. X re-tests the IS-informative
    /// cross-sectional signals on the ≥ $50M median-volume universe (cheaper cost bucket); M combines the six
    /// IS-informative signals into an equal-weight rank composite with signs taken from the IS screen.
    /// </summary>
    public static IReadOnlyList<FamilySpec> Extension { get; } = BuildExtension();

    public static IEnumerable<FamilySpec> Everything => All.Concat(Extension);

    public const double LiquidMinimumQuoteVolume = 50_000_000d;

    private static List<FamilySpec> BuildExtension()
    {
        var families = new List<FamilySpec>();
        {
            var grid = new List<AlphaConfig>();
            ModelFactory rev = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.F.Residual(c, t, I(p, "lb")));
            ModelFactory carry = (ctx, p) => Xs(ctx, p, (c, t) => ctx.F.FundingSum(c, t, W));
            ModelFactory lowVol = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.F.Vol(c, t, I(p, "lb")));
            ModelFactory taker = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.F.TakerImbalance(c, t, D));
            foreach (var lb in new[] { 72, W, M })
            {
                foreach (var hold in new[] { 72, W })
                {
                    grid.Add(new AlphaConfig("X", "liquid-reversal", "XS", P(("lb", lb), ("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), rev, ["lb", "hold", "q"]));
                }
            }

            foreach (var hold in new[] { 72, W })
            {
                grid.Add(new AlphaConfig("X", "liquid-funding-level", "XS", P(("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), carry, ["hold", "q"]));
                grid.Add(new AlphaConfig("X", "liquid-taker-contrarian", "XS", P(("step", D), ("hold", hold == W ? D : 72), ("q", 0.2), ("sign", 1)), taker, ["hold", "q"]));
            }

            foreach (var lb in new[] { W, M })
            {
                grid.Add(new AlphaConfig("X", "liquid-low-vol", "XS", P(("lb", lb), ("step", D), ("hold", W), ("q", 0.2), ("sign", 1)), lowVol, ["lb", "q"]));
            }

            var screens = new List<ScreenSpec>
            {
                new("-resid72->24", "XS", D, D, ctx => (c, t) => -ctx.F.Residual(c, t, 72), null),
                new("-resid168->168", "XS", W, W, ctx => (c, t) => -ctx.F.Residual(c, t, W), null),
                new("funding7d->168", "XS", W, W, ctx => (c, t) => ctx.F.FundingSum(c, t, W), null),
                new("-taker24->24", "XS", D, D, ctx => (c, t) => -ctx.F.TakerImbalance(c, t, D), null),
                new("-vol720->168", "XS", W, W, ctx => (c, t) => -ctx.F.Vol(c, t, M), null)
            };
            families.Add(new FamilySpec("X", "Liquid-universe cross-section (phase 2b)", "P1", "1d rebalance",
                "Same mechanisms as A/B/C/F/I; restricting to coins with ≥ $50M median daily volume lowers half-spread and impact.",
                "The IS-informative cross-sectional signals stay informative on liquid coins and clear CONSERVATIVE costs at 3–7 day holds.",
                "1h Vision klines + funding", false, screens, grid, LiquidUniverse: true));
        }

        {
            var grid = new List<AlphaConfig>();
            ModelFactory composite = (ctx, p) =>
            {
                var c = p["liq"] == 1 ? ctx.Liquid! : ctx;
                var signal = Composite(c);
                return new CrossSectionalModel(c.U, (coin, t) => p["sign"] * signal(coin, t), I(p, "step"), I(p, "hold"), p["q"]);
            };
            foreach (var liq in new[] { 0d, 1d })
            {
                foreach (var hold in new[] { D, 72, W })
                {
                    grid.Add(new AlphaConfig("M", "rank-composite", "XS", P(("liq", liq), ("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), composite, ["hold", "q"]));
                }
            }

            var screens = new List<ScreenSpec>
            {
                new("composite->24", "XS", D, D, ctx => Composite(ctx), null),
                new("composite->72", "XS", 72, 72, ctx => Composite(ctx), null),
                new("composite->168", "XS", W, W, ctx => Composite(ctx), null),
                new("composite liquid->168", "XS", W, W, ctx => Composite(ctx.Liquid!), null)
            };
            families.Add(new FamilySpec("M", "Linear rank composite (phase 2b)", "P1", "1d rebalance",
                "Weakly correlated IS-informative signals (reversal, low vol, taker contrarian, funding/price divergence, funding level, volume contrarian) diversify each other's noise.",
                "An equal-weight composite of cross-sectional ranks, signs fixed from the IS screen, has a higher IS IC than its parts and clears CONSERVATIVE costs at 1–7 day holds.",
                "1h Vision klines + funding", false, screens, grid));
        }

        return families;
    }

    /// <summary>Equal-weight mean of six cross-sectional ranks (at least four present). Signs come from the phase 2 IS screen.</summary>
    private static CoinSignal Composite(AlphaContext ctx)
    {
        var parts = new[]
        {
            new XsRanker(ctx.U, (c, t) => -ctx.F.VolAdjustedRet(c, t, W)),
            new XsRanker(ctx.U, (c, t) => -ctx.F.Vol(c, t, M)),
            new XsRanker(ctx.U, (c, t) => -ctx.F.TakerImbalance(c, t, D)),
            new XsRanker(ctx.U, (c, t) => -(ctx.F.VolAdjustedRet(c, t, 72) - ctx.F.FundingZ(c, t))),
            new XsRanker(ctx.U, (c, t) => ctx.F.FundingSum(c, t, W)),
            new XsRanker(ctx.U, (c, t) => -ctx.F.VolumeRatio(c, t, W))
        };
        return (c, t) =>
        {
            var sum = 0d;
            var n = 0;
            foreach (var part in parts)
            {
                var r = part.Rank(c, t);
                if (!double.IsNaN(r))
                {
                    sum += r;
                    n++;
                }
            }

            return n >= 4 ? sum / n : double.NaN;
        };
    }

    private static SortedDictionary<string, double> P(params (string Key, double Value)[] values)
    {
        var d = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var (k, v) in values)
        {
            d[k] = v;
        }

        return d;
    }

    private static int I(IReadOnlyDictionary<string, double> p, string key) => (int)Math.Round(p[key]);

    private static double S(IReadOnlyDictionary<string, double> p) => p.TryGetValue("sign", out var s) ? s : 1d;

    private static ITargetModel Xs(AlphaContext ctx, IReadOnlyDictionary<string, double> p, CoinSignal signal, HourGate? gate = null)
    {
        var sign = S(p);
        return new CrossSectionalModel(ctx.U, (c, t) => sign * signal(c, t), I(p, "step"), I(p, "hold"), p.TryGetValue("q", out var q) ? q : 0.2, gate: gate);
    }

    private static ITargetModel Ev(AlphaContext ctx, IReadOnlyDictionary<string, double> p, EventTrigger trigger, HourGate? gate = null)
    {
        var sign = (int)S(p);
        return new EventModel(ctx.U, (c, t) => sign * trigger(c, t), I(p, "step"), I(p, "hold"), gate: gate);
    }

    private static List<FamilySpec> Build()
    {
        var families = new List<FamilySpec>();

        // A: cross-sectional momentum.
        {
            var grid = new List<AlphaConfig>();
            ModelFactory raw = (ctx, p) => Xs(ctx, p, (c, t) => ctx.F.Ret(c, t, I(p, "lb")));
            ModelFactory voladj = (ctx, p) => Xs(ctx, p, (c, t) => ctx.F.VolAdjustedRet(c, t, I(p, "lb")));
            ModelFactory resid = (ctx, p) => Xs(ctx, p, (c, t) => ctx.F.Residual(c, t, I(p, "lb")));
            ModelFactory multi = (ctx, p) => Xs(ctx, p, (c, t) => ctx.F.VolAdjustedRet(c, t, 72) + ctx.F.VolAdjustedRet(c, t, W) + ctx.F.VolAdjustedRet(c, t, M));
            foreach (var (name, factory) in new[] { ("raw", raw), ("voladj", voladj), ("resid", resid) })
            {
                foreach (var lb in new[] { 72, W, M })
                {
                    foreach (var hold in new[] { D, W })
                    {
                        grid.Add(new AlphaConfig("A", name + "-1d", "XS", P(("lb", lb), ("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), factory, ["lb", "hold", "q"]));
                    }
                }
            }

            foreach (var hold in new[] { D, W })
            {
                grid.Add(new AlphaConfig("A", "multi-1d", "XS", P(("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), multi, ["hold", "q"]));
            }

            foreach (var (name, factory) in new[] { ("raw", raw), ("resid", resid) })
            {
                foreach (var lb in new[] { D, 72 })
                {
                    foreach (var hold in new[] { 4, D })
                    {
                        grid.Add(new AlphaConfig("A", name + "-4h", "XS", P(("lb", lb), ("step", 4), ("hold", hold), ("q", 0.2), ("sign", 1)), factory, ["lb", "hold", "q"]));
                    }
                }
            }

            var screens = new List<ScreenSpec>();
            foreach (var lb in new[] { D, 72, W, M })
            {
                screens.Add(new ScreenSpec($"ret{lb}->24", "XS", D, D, ctx => (c, t) => ctx.F.Ret(c, t, lb), null));
                screens.Add(new ScreenSpec($"ret{lb}->168", "XS", W, W, ctx => (c, t) => ctx.F.Ret(c, t, lb), null));
                screens.Add(new ScreenSpec($"resid{lb}->24", "XS", D, D, ctx => (c, t) => ctx.F.Residual(c, t, lb), null));
                screens.Add(new ScreenSpec($"voladj{lb}->24", "XS", D, D, ctx => (c, t) => ctx.F.VolAdjustedRet(c, t, lb), null));
            }

            screens.Add(new ScreenSpec("ret1->1", "XS", 1, 1, ctx => (c, t) => ctx.F.Ret(c, t, 1), null));
            screens.Add(new ScreenSpec("ret4->4", "XS", 4, 4, ctx => (c, t) => ctx.F.Ret(c, t, 4), null));
            families.Add(new FamilySpec("A", "Cross-sectional momentum", "P0", "1d and 4h rebalance",
                "Slow diffusion of information and trend-chasing flows make recent cross-sectional winners keep outperforming losers.",
                "Coins with the highest trailing return (raw, vol-adjusted, BTC-residual, or a multi-horizon blend) outperform the lowest over the next 1–7 days.",
                "1h Vision klines", false, screens, grid));
        }

        // B: cross-sectional reversal.
        {
            var grid = new List<AlphaConfig>();
            ModelFactory raw = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.F.Ret(c, t, I(p, "lb")));
            ModelFactory resid = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.F.Residual(c, t, I(p, "lb")));
            ModelFactory lowVol = (ctx, p) => Xs(ctx, p, (c, t) =>
            {
                var r = ctx.F.Residual(c, t, I(p, "lb"));
                var v = ctx.F.VolumeRatio(c, t, I(p, "lb"));
                return double.IsNaN(v) || v <= 0 ? double.NaN : -r / v;
            });
            foreach (var (name, factory) in new[] { ("raw", raw), ("resid", resid), ("lowvolume", lowVol) })
            {
                foreach (var lb in new[] { 1, 4, D })
                {
                    foreach (var hold in new[] { 4, 12, D })
                    {
                        grid.Add(new AlphaConfig("B", name, "XS", P(("lb", lb), ("step", 4), ("hold", hold), ("q", 0.2), ("sign", 1)), factory, ["lb", "hold", "q"]));
                    }
                }
            }

            var screens = new List<ScreenSpec>();
            foreach (var lb in new[] { 1, 4, D })
            {
                foreach (var h in new[] { 4, D })
                {
                    screens.Add(new ScreenSpec($"-ret{lb}->{h}", "XS", h, 4, ctx => (c, t) => -ctx.F.Ret(c, t, lb), null));
                    screens.Add(new ScreenSpec($"-resid{lb}->{h}", "XS", h, 4, ctx => (c, t) => -ctx.F.Residual(c, t, lb), null));
                }
            }

            families.Add(new FamilySpec("B", "Cross-sectional reversal", "P0", "4h rebalance",
                "Liquidity-driven overshoots in individual coins (relative to BTC) are absorbed by liquidity providers and partly reverse.",
                "Coins with the most negative recent (1–24h) BTC-residual return outperform the most positive over the next 4–24h; reversal is stronger on low-volume moves.",
                "1h Vision klines", false, screens, grid));
        }

        // C: funding.
        {
            var grid = new List<AlphaConfig>();
            ModelFactory carry = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.F.FundingSum(c, t, I(p, "lb")));
            ModelFactory z = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.F.FundingZ(c, t));
            ModelFactory change = (ctx, p) => Xs(ctx, p, (c, t) => -(ctx.F.FundingSum(c, t, D) - ctx.F.FundingSum(c, t - D, D)));
            ModelFactory div = (ctx, p) => Xs(ctx, p, (c, t) => ctx.F.VolAdjustedRet(c, t, 72) - ctx.F.FundingZ(c, t));
            ModelFactory fade = (ctx, p) => Ev(ctx, p, (c, t) =>
            {
                var fz = ctx.F.FundingZ(c, t);
                return double.IsNaN(fz) ? 0 : fz >= p["k"] ? -1 : fz <= -p["k"] ? 1 : 0;
            });
            foreach (var hold in new[] { D, 72, W })
            {
                foreach (var q in new[] { 0.1, 0.2 })
                {
                    grid.Add(new AlphaConfig("C", "carry7d", "XS", P(("lb", W), ("step", D), ("hold", hold), ("q", q), ("sign", 1)), carry, ["lb", "hold", "q"]));
                }

                grid.Add(new AlphaConfig("C", "fundingz", "XS", P(("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), z, ["hold", "q"]));
            }

            foreach (var hold in new[] { 8, D })
            {
                foreach (var q in new[] { 0.1, 0.2 })
                {
                    grid.Add(new AlphaConfig("C", "carry24h", "XS", P(("lb", D), ("step", 8), ("hold", hold), ("q", q), ("sign", 1)), carry, ["lb", "hold", "q"]));
                }

                grid.Add(new AlphaConfig("C", "change", "XS", P(("step", 8), ("hold", hold), ("q", 0.2), ("sign", 1)), change, ["hold", "q"]));
            }

            foreach (var hold in new[] { D, 72 })
            {
                grid.Add(new AlphaConfig("C", "divergence", "XS", P(("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), div, ["hold", "q"]));
            }

            foreach (var k in new[] { 2d, 3d })
            {
                foreach (var hold in new[] { 8, D, 72 })
                {
                    grid.Add(new AlphaConfig("C", "extreme-fade", "EVENT", P(("k", k), ("step", 8), ("hold", hold), ("sign", 1)), fade, ["k", "hold"]));
                }
            }

            var screens = new List<ScreenSpec>
            {
                new("-funding7d->24", "XS", D, D, ctx => (c, t) => -ctx.F.FundingSum(c, t, W), null),
                new("-funding7d->168", "XS", W, W, ctx => (c, t) => -ctx.F.FundingSum(c, t, W), null),
                new("-funding24h->8", "XS", 8, 8, ctx => (c, t) => -ctx.F.FundingSum(c, t, D), null),
                new("-funding24h->24", "XS", D, D, ctx => (c, t) => -ctx.F.FundingSum(c, t, D), null),
                new("-fundingz->24", "XS", D, D, ctx => (c, t) => -ctx.F.FundingZ(c, t), null),
                new("-fundingz->72", "XS", 72, 72, ctx => (c, t) => -ctx.F.FundingZ(c, t), null),
                new("-dfunding->24", "XS", D, D, ctx => (c, t) => -(ctx.F.FundingSum(c, t, D) - ctx.F.FundingSum(c, t - D, D)), null),
                new("divergence->24", "XS", D, D, ctx => (c, t) => ctx.F.VolAdjustedRet(c, t, 72) - ctx.F.FundingZ(c, t), null),
                new("fade z>=2 ->24", "EVENT", D, 8, null, ctx => (c, t) => { var z = ctx.F.FundingZ(c, t); return double.IsNaN(z) ? 0 : z >= 2 ? -1 : z <= -2 ? 1 : 0; }),
                new("fade z>=3 ->72", "EVENT", 72, 8, null, ctx => (c, t) => { var z = ctx.F.FundingZ(c, t); return double.IsNaN(z) ? 0 : z >= 3 ? -1 : z <= -3 ? 1 : 0; })
            };
            families.Add(new FamilySpec("C", "Funding and price divergence", "P0", "8h / 1d",
                "Persistent positive funding marks crowded leveraged longs that pay carry; unwinds and carry both favour the short side. Price strength without funding strength is spot-led and more durable.",
                "Low-funding coins outperform high-funding coins net of the funding they pay or receive; extreme funding z-scores mean-revert in price; price/funding divergence predicts continuation of the spot-led side.",
                "Vision monthly fundingRate", false, screens, grid));
        }

        // F: volatility regimes.
        {
            var grid = new List<AlphaConfig>();
            ModelFactory breakout = (ctx, p) => Ev(ctx, p, (c, t) =>
            {
                var fast = ctx.F.Vol(c, t - 1, D);
                var slow = ctx.F.Vol(c, t - 1, M);
                if (double.IsNaN(fast) || double.IsNaN(slow) || fast / slow >= p["c"])
                {
                    return 0;
                }

                var close = ctx.P.Close[c][t];
                return close > ctx.F.PriorHigh(c, t, W) ? 1 : close < ctx.F.PriorLow(c, t, W) ? -1 : 0;
            });
            ModelFactory revert = (ctx, p) => Ev(ctx, p, (c, t) =>
            {
                var r = ctx.F.Residual(c, t, D);
                var v = ctx.F.Vol(c, t, M);
                if (double.IsNaN(r) || double.IsNaN(v))
                {
                    return 0;
                }

                var z = r / (v * Math.Sqrt(D));
                return z >= p["k"] ? -1 : z <= -p["k"] ? 1 : 0;
            });
            ModelFactory lowVol = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.F.Vol(c, t, I(p, "lb")));
            foreach (var cut in new[] { 0.5, 0.7 })
            {
                foreach (var hold in new[] { D, 72 })
                {
                    grid.Add(new AlphaConfig("F", "compression-breakout", "EVENT", P(("c", cut), ("step", 4), ("hold", hold), ("sign", 1)), breakout, ["c", "hold"]));
                }
            }

            foreach (var k in new[] { 2d, 3d })
            {
                foreach (var hold in new[] { D, 72 })
                {
                    grid.Add(new AlphaConfig("F", "shock-revert", "EVENT", P(("k", k), ("step", 4), ("hold", hold), ("sign", 1)), revert, ["k", "hold"]));
                }
            }

            foreach (var lb in new[] { W, M })
            {
                grid.Add(new AlphaConfig("F", "low-vol", "XS", P(("lb", lb), ("step", D), ("hold", W), ("q", 0.2), ("sign", 1)), lowVol, ["lb", "q"]));
            }

            var screens = new List<ScreenSpec>
            {
                new("-vol168->168", "XS", W, W, ctx => (c, t) => -ctx.F.Vol(c, t, W), null),
                new("-vol720->168", "XS", W, W, ctx => (c, t) => -ctx.F.Vol(c, t, M), null),
                new("compression<0.5 breakout ->24", "EVENT", D, 4, null, ctx => (c, t) =>
                {
                    var fast = ctx.F.Vol(c, t - 1, D);
                    var slow = ctx.F.Vol(c, t - 1, M);
                    if (double.IsNaN(fast) || double.IsNaN(slow) || fast / slow >= 0.5) return 0;
                    var close = ctx.P.Close[c][t];
                    return close > ctx.F.PriorHigh(c, t, W) ? 1 : close < ctx.F.PriorLow(c, t, W) ? -1 : 0;
                }),
                new("compression<0.7 breakout ->72", "EVENT", 72, 4, null, ctx => (c, t) =>
                {
                    var fast = ctx.F.Vol(c, t - 1, D);
                    var slow = ctx.F.Vol(c, t - 1, M);
                    if (double.IsNaN(fast) || double.IsNaN(slow) || fast / slow >= 0.7) return 0;
                    var close = ctx.P.Close[c][t];
                    return close > ctx.F.PriorHigh(c, t, W) ? 1 : close < ctx.F.PriorLow(c, t, W) ? -1 : 0;
                }),
                new("resid shock z>=2 fade ->24", "EVENT", D, 4, null, ctx => (c, t) =>
                {
                    var r = ctx.F.Residual(c, t, D);
                    var v = ctx.F.Vol(c, t, M);
                    if (double.IsNaN(r) || double.IsNaN(v)) return 0;
                    var z = r / (v * Math.Sqrt(D));
                    return z >= 2 ? -1 : z <= -2 ? 1 : 0;
                }),
                new("resid shock z>=3 fade ->72", "EVENT", 72, 4, null, ctx => (c, t) =>
                {
                    var r = ctx.F.Residual(c, t, D);
                    var v = ctx.F.Vol(c, t, M);
                    if (double.IsNaN(r) || double.IsNaN(v)) return 0;
                    var z = r / (v * Math.Sqrt(D));
                    return z >= 3 ? -1 : z <= -3 ? 1 : 0;
                })
            };
            families.Add(new FamilySpec("F", "Volatility regimes", "P0", "4h / 1d",
                "Volatility clusters: compression precedes expansion, and the direction of the first range break carries information; extreme idiosyncratic moves overshoot; lottery-like high-vol coins are overpriced.",
                "Range breaks after vol compression continue; ≥2σ 24h BTC-residual shocks partly revert within 1–3 days; low-vol coins outperform high-vol coins cross-sectionally.",
                "1h Vision klines", false, screens, grid));
        }

        // H: BTC -> alt lead/lag.
        {
            var grid = new List<AlphaConfig>();
            ModelFactory gap = (ctx, p) =>
            {
                var lb = I(p, "lb");
                var k = p["k"];
                return Xs(ctx, p, (c, t) => -ctx.F.Residual(c, t, lb), t => BtcImpulse(ctx, t, lb) >= k);
            };
            foreach (var lb in new[] { 1, 2, 4 })
            {
                foreach (var k in new[] { 1.5, 2.5 })
                {
                    foreach (var hold in new[] { 1, 4 })
                    {
                        grid.Add(new AlphaConfig("H", "catch-up", "XS", P(("lb", lb), ("k", k), ("step", 1), ("hold", hold), ("q", 0.2), ("sign", 1)), gap, ["lb", "k", "hold", "q"]));
                    }
                }
            }

            var screens = new List<ScreenSpec>();
            foreach (var lb in new[] { 1, 2, 4 })
            {
                foreach (var h in new[] { 1, 4 })
                {
                    screens.Add(new ScreenSpec($"gap{lb}|impulse>=1.5 ->{h}", "XS", h, 1, ctx => (c, t) => BtcImpulse(ctx, t, lb) >= 1.5 ? -ctx.F.Residual(c, t, lb) : double.NaN, null));
                }
            }

            screens.Add(new ScreenSpec("btc1h->alt1h (beta-scaled)", "XS", 1, 1, ctx => (c, t) => ctx.F.Beta(c, t) * ctx.F.Ret(ctx.F.Btc, t, 1), null));
            families.Add(new FamilySpec("H", "BTC to alt lead/lag", "P0", "1h",
                "Price discovery happens in BTC first; slower alt books adjust with a lag, so after a BTC impulse the alts that have not yet moved by their beta catch up.",
                "After an hourly BTC move of ≥k σ, coins with the most negative BTC-residual return over the same window outperform the most positive over the next 1–4h.",
                "1h Vision klines", false, screens, grid));
        }

        // E: shock continuation vs reversal (no liquidation data; range/volume proxy).
        {
            var grid = new List<AlphaConfig>();
            ModelFactory shock = (ctx, p) => Ev(ctx, p, (c, t) =>
            {
                var avg = ctx.F.AverageRange(c, t - 1, W);
                var range = ctx.F.BarRange(c, t);
                if (double.IsNaN(avg) || double.IsNaN(range) || range < p["k"] * avg)
                {
                    return 0;
                }

                var vr = ctx.F.VolumeRatio(c, t, 1, W);
                if (double.IsNaN(vr) || vr < 3)
                {
                    return 0;
                }

                var r = ctx.F.Ret(c, t, 1);
                return double.IsNaN(r) || r == 0 ? 0 : Math.Sign(r) * (int)p["mode"];
            });
            foreach (var k in new[] { 3d, 5d })
            {
                foreach (var mode in new[] { 1d, -1d })
                {
                    foreach (var hold in new[] { 4, D })
                    {
                        grid.Add(new AlphaConfig("E", mode > 0 ? "continue" : "fade", "EVENT", P(("k", k), ("mode", mode), ("step", 1), ("hold", hold), ("sign", 1)), shock, ["k", "hold"]));
                    }
                }
            }

            var screens = new List<ScreenSpec>();
            foreach (var k in new[] { 3d, 5d })
            {
                foreach (var h in new[] { 4, D })
                {
                    screens.Add(new ScreenSpec($"shock{k} continue ->{h}", "EVENT", h, 1, null, ctx => (c, t) =>
                    {
                        var avg = ctx.F.AverageRange(c, t - 1, W);
                        var range = ctx.F.BarRange(c, t);
                        if (double.IsNaN(avg) || double.IsNaN(range) || range < k * avg) return 0;
                        var vr = ctx.F.VolumeRatio(c, t, 1, W);
                        if (double.IsNaN(vr) || vr < 3) return 0;
                        var r = ctx.F.Ret(c, t, 1);
                        return double.IsNaN(r) || r == 0 ? 0 : Math.Sign(r);
                    }));
                }
            }

            families.Add(new FamilySpec("E", "Shock / cascade proxy", "P1", "1h",
                "Forced liquidations print as hourly bars with extreme range and volume; once forced flow ends, price either keeps going (information) or snaps back (pure liquidity).",
                "Hourly bars with range ≥k× their 7-day average and volume ≥3× either continue or revert over 4–24h. No liquidation feed exists, so this is a proxy only.",
                "1h Vision klines (range + volume proxy; no liquidation data)", false, screens, grid));
        }

        // I: volume and taker flow.
        {
            var grid = new List<AlphaConfig>();
            ModelFactory taker = (ctx, p) => Xs(ctx, p, (c, t) => ctx.F.TakerImbalance(c, t, I(p, "lb")));
            ModelFactory confirmed = (ctx, p) => Xs(ctx, p, (c, t) =>
            {
                var r = ctx.F.Ret(c, t, D);
                var v = ctx.F.VolumeRatio(c, t, D);
                return double.IsNaN(r) || double.IsNaN(v) ? double.NaN : Math.Sign(r) * v;
            });
            ModelFactory attention = (ctx, p) => Xs(ctx, p, (c, t) => ctx.F.VolumeRatio(c, t, W));
            foreach (var lb in new[] { D, 72 })
            {
                foreach (var hold in new[] { D, 72 })
                {
                    grid.Add(new AlphaConfig("I", "taker", "XS", P(("lb", lb), ("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), taker, ["lb", "hold", "q"]));
                }
            }

            foreach (var hold in new[] { D, 72 })
            {
                grid.Add(new AlphaConfig("I", "volume-confirmed", "XS", P(("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), confirmed, ["hold", "q"]));
            }

            foreach (var hold in new[] { 72, W })
            {
                grid.Add(new AlphaConfig("I", "attention", "XS", P(("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), attention, ["hold", "q"]));
            }

            var screens = new List<ScreenSpec>
            {
                new("taker24->24", "XS", D, D, ctx => (c, t) => ctx.F.TakerImbalance(c, t, D), null),
                new("taker72->72", "XS", 72, 72, ctx => (c, t) => ctx.F.TakerImbalance(c, t, 72), null),
                new("taker4->4", "XS", 4, 4, ctx => (c, t) => ctx.F.TakerImbalance(c, t, 4), null),
                new("signed volume ratio ->24", "XS", D, D, ctx => (c, t) => { var r = ctx.F.Ret(c, t, D); var v = ctx.F.VolumeRatio(c, t, D); return double.IsNaN(r) || double.IsNaN(v) ? double.NaN : Math.Sign(r) * v; }, null),
                new("volume ratio 7d ->168", "XS", W, W, ctx => (c, t) => ctx.F.VolumeRatio(c, t, W), null)
            };
            families.Add(new FamilySpec("I", "Volume and taker-flow anomalies", "P1", "1d / 4h",
                "Aggressive (taker) buying reveals informed demand; abnormal volume confirms the direction of a move; rising attention precedes flows.",
                "Coins with the highest taker-buy share, the strongest volume-confirmed moves, or the largest volume growth outperform over 1–7 days.",
                "1h Vision klines (taker_buy_quote_volume)", false, screens, grid));
        }

        // J: breakout quality.
        {
            var grid = new List<AlphaConfig>();
            ModelFactory breakout = (ctx, p) => Ev(ctx, p, (c, t) =>
            {
                var lb = I(p, "lb");
                var close = ctx.P.Close[c][t];
                var high = ctx.F.PriorHigh(c, t, lb);
                if (double.IsNaN(high) || !(close > high))
                {
                    return 0;
                }

                if (p["v"] > 0)
                {
                    var vr = ctx.F.VolumeRatio(c, t, D);
                    var fast = ctx.F.Vol(c, t - D, 72);
                    var slow = ctx.F.Vol(c, t - D, M);
                    if (double.IsNaN(vr) || vr < p["v"] || double.IsNaN(fast) || double.IsNaN(slow) || fast / slow >= 0.8)
                    {
                        return 0;
                    }
                }

                return 1;
            });
            foreach (var lb in new[] { W, M })
            {
                foreach (var v in new[] { 0d, 1.5, 3d })
                {
                    foreach (var hold in new[] { D, W })
                    {
                        grid.Add(new AlphaConfig("J", v > 0 ? "quality" : "plain", "EVENT", P(("lb", lb), ("v", v), ("step", D), ("hold", hold), ("sign", 1)), breakout, v > 0 ? ["lb", "v", "hold"] : ["lb", "hold"]));
                    }
                }
            }

            var screens = new List<ScreenSpec>();
            foreach (var lb in new[] { W, M })
            {
                foreach (var v in new[] { 0d, 1.5 })
                {
                    screens.Add(new ScreenSpec($"break{lb} v>={v} ->72", "EVENT", 72, D, null, ctx => (c, t) =>
                    {
                        var close = ctx.P.Close[c][t];
                        var high = ctx.F.PriorHigh(c, t, lb);
                        if (double.IsNaN(high) || !(close > high)) return 0;
                        if (v > 0)
                        {
                            var vr = ctx.F.VolumeRatio(c, t, D);
                            var fast = ctx.F.Vol(c, t - D, 72);
                            var slow = ctx.F.Vol(c, t - D, M);
                            if (double.IsNaN(vr) || vr < v || double.IsNaN(fast) || double.IsNaN(slow) || fast / slow >= 0.8) return 0;
                        }

                        return 1;
                    }));
                }
            }

            families.Add(new FamilySpec("J", "Breakout quality", "P1", "1d close",
                "Breakouts to new highs that come out of a quiet base on heavy volume reflect new demand; plain breakouts in noisy markets are mostly false.",
                "Daily closes above the prior 7d/30d high continue over 1–7 days, and the volume + compression filter improves the hit rate.",
                "1h Vision klines", false, screens, grid));
        }

        // K: multi-timeframe.
        {
            var grid = new List<AlphaConfig>();
            ModelFactory mtf = (ctx, p) => Ev(ctx, p, (c, t) =>
            {
                var r1 = ctx.F.Ret(c, t, 1);
                if (double.IsNaN(r1) || r1 == 0)
                {
                    return 0;
                }

                var pull = ctx.F.Ret(c, t - 1, D);
                var v = ctx.F.Vol(c, t, M);
                if (double.IsNaN(pull) || double.IsNaN(v))
                {
                    return 0;
                }

                var z = pull / (v * Math.Sqrt(D));
                var sma = ctx.F.Sma(c, t, I(p, "trend"));
                var close = ctx.P.Close[c][t];
                if (double.IsNaN(sma))
                {
                    return 0;
                }

                if (close > sma && z <= -p["k"] && r1 > 0)
                {
                    return 1;
                }

                return close < sma && z >= p["k"] && r1 < 0 ? -1 : 0;
            });
            foreach (var trend in new[] { 480, 1200 })
            {
                foreach (var k in new[] { 1d, 2d })
                {
                    foreach (var hold in new[] { D, 72 })
                    {
                        grid.Add(new AlphaConfig("K", "trend-pullback-trigger", "EVENT", P(("trend", trend), ("k", k), ("step", 1), ("hold", hold), ("sign", 1)), mtf, ["trend", "k", "hold"]));
                    }
                }
            }

            var screens = new List<ScreenSpec>();
            foreach (var k in new[] { 1d, 2d })
            {
                screens.Add(new ScreenSpec($"sma1200 pullback z{k} trigger ->24", "EVENT", D, 1, null, ctx => (c, t) =>
                {
                    var r1 = ctx.F.Ret(c, t, 1);
                    if (double.IsNaN(r1) || r1 == 0) return 0;
                    var pull = ctx.F.Ret(c, t - 1, D);
                    var v = ctx.F.Vol(c, t, M);
                    if (double.IsNaN(pull) || double.IsNaN(v)) return 0;
                    var z = pull / (v * Math.Sqrt(D));
                    var sma = ctx.F.Sma(c, t, 1200);
                    var close = ctx.P.Close[c][t];
                    if (double.IsNaN(sma)) return 0;
                    if (close > sma && z <= -k && r1 > 0) return 1;
                    return close < sma && z >= k && r1 < 0 ? -1 : 0;
                }));
            }

            families.Add(new FamilySpec("K", "Multi-timeframe", "P1", "1h trigger / 4h-1d setup / 50d trend",
                "Pullbacks inside an established trend are liquidity events; when the hourly bar turns back in the trend direction the pullback is likely over.",
                "In a 20d/50d uptrend, a ≥kσ 24h pullback followed by an up hour precedes 1–3 day gains (mirror for downtrends).",
                "1h Vision klines", false, screens, grid));
        }

        // D: open interest and positioning (Vision daily metrics, 2024-01 onward, own split).
        {
            var grid = new List<AlphaConfig>();
            ModelFactory oi = (ctx, p) => Xs(ctx, p, (c, t) => ctx.Metrics!.OiChange(c, t, I(p, "lb")));
            ModelFactory quadrant = (ctx, p) => Xs(ctx, p, (c, t) =>
            {
                var r = ctx.F.Ret(c, t, I(p, "lb"));
                var o = ctx.Metrics!.OiChange(c, t, I(p, "lb"));
                return double.IsNaN(r) || double.IsNaN(o) ? double.NaN : -Math.Sign(r) * o;
            });
            ModelFactory top = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.Metrics!.Z(ctx.Metrics.TopTraderRatio, c, t));
            ModelFactory crowd = (ctx, p) => Xs(ctx, p, (c, t) => -ctx.Metrics!.Z(ctx.Metrics.GlobalRatio, c, t));
            ModelFactory taker = (ctx, p) => Xs(ctx, p, (c, t) => ctx.Metrics!.Mean(ctx.Metrics.TakerRatio, c, t, I(p, "lb")));
            foreach (var lb in new[] { D, 72 })
            {
                foreach (var hold in new[] { D, 72 })
                {
                    grid.Add(new AlphaConfig("D", "oi-change", "XS", P(("lb", lb), ("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), oi, ["lb", "hold", "q"]));
                    grid.Add(new AlphaConfig("D", "price-oi-quadrant", "XS", P(("lb", lb), ("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), quadrant, ["lb", "hold", "q"]));
                    grid.Add(new AlphaConfig("D", "taker-ratio", "XS", P(("lb", lb), ("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), taker, ["lb", "hold", "q"]));
                }
            }

            foreach (var hold in new[] { D, 72 })
            {
                grid.Add(new AlphaConfig("D", "toptrader-contrarian", "XS", P(("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), top, ["hold", "q"]));
                grid.Add(new AlphaConfig("D", "crowd-contrarian", "XS", P(("step", D), ("hold", hold), ("q", 0.2), ("sign", 1)), crowd, ["hold", "q"]));
            }

            var screens = new List<ScreenSpec>
            {
                new("oi24->24", "XS", D, D, ctx => (c, t) => ctx.Metrics!.OiChange(c, t, D), null),
                new("oi72->72", "XS", 72, 72, ctx => (c, t) => ctx.Metrics!.OiChange(c, t, 72), null),
                new("-sign(ret24)*oi24 ->24", "XS", D, D, ctx => (c, t) => { var r = ctx.F.Ret(c, t, D); var o = ctx.Metrics!.OiChange(c, t, D); return double.IsNaN(r) || double.IsNaN(o) ? double.NaN : -Math.Sign(r) * o; }, null),
                new("-toptrader z ->24", "XS", D, D, ctx => (c, t) => -ctx.Metrics!.Z(ctx.Metrics.TopTraderRatio, c, t), null),
                new("-global ls z ->24", "XS", D, D, ctx => (c, t) => -ctx.Metrics!.Z(ctx.Metrics.GlobalRatio, c, t), null),
                new("taker ratio 24 ->24", "XS", D, D, ctx => (c, t) => ctx.Metrics!.Mean(ctx.Metrics.TakerRatio, c, t, D), null)
            };
            families.Add(new FamilySpec("D", "Open interest and positioning", "P0", "1d",
                "Open-interest growth shows new leverage entering; when it rises against price (shorts adding into a rally or longs into a fall) the side adding leverage gets squeezed. Crowded retail long/short ratios are contrarian.",
                "Coins whose price moved against their OI change, and coins where retail and top traders are most net-short, outperform over 1–3 days. Sign of plain OI change is decided by the IS screen.",
                "Vision daily metrics (sum_open_interest_value, long/short ratios, taker ratio), 2024-01 → 2026-09", true, screens, grid));
        }

        return families;
    }

    /// <summary>|BTC return over the last L hours| in units of its 30-day hourly vol × sqrt(L).</summary>
    public static double BtcImpulse(AlphaContext ctx, int t, int lookback)
    {
        var btc = ctx.F.Btc;
        var r = ctx.F.Ret(btc, t, lookback);
        var v = ctx.F.Vol(btc, t - lookback, M);
        return double.IsNaN(r) || double.IsNaN(v) || v <= 0 ? 0 : Math.Abs(r) / (v * Math.Sqrt(lookback));
    }

    /// <summary>Regime gates for family G, all causal. Medians use 90 daily samples before t.</summary>
    public static IReadOnlyList<(string Name, Func<AlphaContext, HourGate> Gate)> Regimes { get; } =
    [
        ("btc-uptrend", ctx => t => ctx.P.Close[ctx.F.Btc][t] > ctx.F.Sma(ctx.F.Btc, t, M)),
        ("btc-downtrend", ctx => t => ctx.P.Close[ctx.F.Btc][t] < ctx.F.Sma(ctx.F.Btc, t, M)),
        ("breadth-high", ctx => t => ctx.F.Market(t, W).Breadth > 0.5),
        ("breadth-low", ctx => t => ctx.F.Market(t, W).Breadth <= 0.5),
        ("dispersion-high", ctx => t => AboveTrailingMedian(ctx, t, m => m.Dispersion, D) == true),
        ("dispersion-low", ctx => t => AboveTrailingMedian(ctx, t, m => m.Dispersion, D) == false),
        ("funding-high", ctx => t => AboveTrailingMedian(ctx, t, m => m.MedianFunding24h, D) == true),
        ("funding-low", ctx => t => AboveTrailingMedian(ctx, t, m => m.MedianFunding24h, D) == false)
    ];

    private static bool? AboveTrailingMedian(AlphaContext ctx, int t, Func<MarketState, double> pick, int lookback)
    {
        var now = pick(ctx.F.Market(t, lookback));
        if (double.IsNaN(now))
        {
            return null;
        }

        var history = new List<double>(90);
        for (var k = 1; k <= 90; k++)
        {
            var at = t - 24 * k;
            if (at < 0)
            {
                break;
            }

            var v = pick(ctx.F.Market(at, lookback));
            if (!double.IsNaN(v))
            {
                history.Add(v);
            }
        }

        if (history.Count < 60)
        {
            return null;
        }

        history.Sort();
        return now > history[history.Count / 2];
    }
}
