namespace TradingPlatform.Research.Framework;

public static class ResearchStatuses
{
    public const string Rejected = "REJECTED";
    public const string Weak = "WEAK";
    public const string Research = "RESEARCH";
    public const string Promising = "PROMISING";
    public const string PaperCandidate = "PAPER_CANDIDATE";
    public const string LiveCandidate = "LIVE_CANDIDATE";
}

/// <summary>All PF inputs are net of fees, slippage and funding.</summary>
public sealed record VerdictInputs(
    int ValidationTrades,
    decimal? ValidationProfitFactorConservative,
    double? DeflatedSharpeProbability,
    decimal? Breadth,
    bool? Stable,
    decimal? OosProfitFactorConservative = null,
    decimal? StressProfitFactor = null,
    bool OosOpenedOnce = false,
    int ForwardWeeks = 0,
    bool ForwardMatchesBacktest = false,
    bool SurvivorshipBiased = true);

public sealed record Verdict(string Status, IReadOnlyList<string> Reasons);

public static class ResearchVerdict
{
    public const int MinimumValidationTrades = 30;
    public const decimal PromisingProfitFactor = 1.15m;
    public const double PromisingDeflatedSharpe = 0.90;
    public const decimal PromisingBreadth = 0.55m;
    public const decimal PaperOosProfitFactor = 1.05m;
    public const decimal PaperStressProfitFactor = 1.0m;
    public const int LiveForwardWeeks = 4;

    public static Verdict Assign(VerdictInputs x)
    {
        var reasons = new List<string>();
        if (x.SurvivorshipBiased)
        {
            reasons.Add("Universe is current listings only (survivorship).");
        }

        var pf = x.ValidationProfitFactorConservative;
        if (x.ValidationTrades < MinimumValidationTrades)
        {
            if (x.ValidationTrades >= 10 && pf is < 0.9m)
            {
                reasons.Add($"Validation PF {pf:0.00} on {x.ValidationTrades} trades.");
                return new Verdict(ResearchStatuses.Rejected, reasons);
            }

            reasons.Add($"Only {x.ValidationTrades} validation trades (< {MinimumValidationTrades}).");
            return new Verdict(ResearchStatuses.Research, reasons);
        }

        if (pf is null or < 1.0m)
        {
            reasons.Add($"Validation PF under CONSERVATIVE costs is {(pf is null ? "n/a" : pf.Value.ToString("0.00"))} (< 1.00).");
            return new Verdict(ResearchStatuses.Rejected, reasons);
        }

        var gaps = new List<string>();
        if (pf <= PromisingProfitFactor)
        {
            gaps.Add($"Validation PF {pf:0.00} ≤ {PromisingProfitFactor:0.00}");
        }

        if (x.DeflatedSharpeProbability is not { } dsr || dsr <= PromisingDeflatedSharpe)
        {
            gaps.Add($"DSR {(x.DeflatedSharpeProbability is { } d ? d.ToString("0.00") : "n/a")} ≤ {PromisingDeflatedSharpe:0.00}");
        }

        if (x.Breadth is not { } breadth || breadth <= PromisingBreadth)
        {
            gaps.Add($"breadth {(x.Breadth is { } b ? b.ToString("P0") : "n/a")} ≤ {PromisingBreadth:P0}");
        }

        if (x.Stable != true)
        {
            gaps.Add(x.Stable is null ? "±20% neighbours not tested" : "±20% neighbours not stable");
        }

        if (gaps.Count > 0)
        {
            reasons.Add(string.Join("; ", gaps) + ".");
            return new Verdict(ResearchStatuses.Weak, reasons);
        }

        var paperGaps = new List<string>();
        if (!x.OosOpenedOnce || x.OosProfitFactorConservative is null)
        {
            paperGaps.Add("sealed OOS not opened");
        }
        else if (x.OosProfitFactorConservative < PaperOosProfitFactor)
        {
            paperGaps.Add($"OOS PF {x.OosProfitFactorConservative:0.00} < {PaperOosProfitFactor:0.00}");
        }

        if (x.StressProfitFactor is not { } stress || stress < PaperStressProfitFactor)
        {
            paperGaps.Add($"STRESS PF {(x.StressProfitFactor is { } s ? s.ToString("0.00") : "n/a")} < {PaperStressProfitFactor:0.00}");
        }

        if (paperGaps.Count > 0)
        {
            reasons.Add(string.Join("; ", paperGaps) + ".");
            return new Verdict(ResearchStatuses.Promising, reasons);
        }

        if (x.ForwardWeeks < LiveForwardWeeks || !x.ForwardMatchesBacktest)
        {
            reasons.Add($"Needs ≥ {LiveForwardWeeks} weeks of Shadow/Testnet forward results matching the backtest (have {x.ForwardWeeks}).");
            return new Verdict(ResearchStatuses.PaperCandidate, reasons);
        }

        reasons.Add("Forward results match the backtest. Live activation is still a human decision.");
        return new Verdict(ResearchStatuses.LiveCandidate, reasons);
    }
}
