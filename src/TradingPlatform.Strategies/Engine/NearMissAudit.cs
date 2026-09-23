namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Deterministic Phase 8 near-miss screen. Numbers are the frozen report table.
/// A row is selected when validation has at least 40 trades, profit factor above 1, and net above 0,
/// or out-of-sample has at least 40 trades, profit factor above 1, and net above 0.
/// Selection does not retune definitions and is not VALIDATED_FOR_PAPER.
/// </summary>
public static class NearMissAudit
{
    public const string Status = "NEAR_MISS";

    public readonly record struct Row(
        string HypothesisId,
        string Family,
        string Variant,
        int IsTrades,
        decimal IsProfitFactor,
        decimal IsNet,
        int ValidationTrades,
        decimal ValidationProfitFactor,
        decimal ValidationNet,
        int OosTrades,
        decimal OosProfitFactor,
        decimal OosNet,
        string Phase8Status);

    public static bool Selected(Row row) =>
        (row.ValidationTrades >= 40 && row.ValidationProfitFactor > 1m && row.ValidationNet > 0m)
        || (row.OosTrades >= 40 && row.OosProfitFactor > 1m && row.OosNet > 0m);

    public static string TemplateKey(Row row) =>
        $"cpa_near_miss_{row.Family.ToLowerInvariant()}_{row.Variant.ToLowerInvariant()}_5m";

    public static IReadOnlyList<Row> Phase8 { get; } =
    [
        R("CPA-SWEEP|BASELINE|5m", "SWEEP", "BASELINE", 5092, 0.78768084m, -3755.04m, 1445, 0.92292531m, -416.75m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-SWEEP|CONTEXTUAL|5m", "SWEEP", "CONTEXTUAL", 35, 0.35953122m, -104.30m, 88, 1.14807955m, 44.30m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-SWEEP|STRICT|5m", "SWEEP", "STRICT", 34, 0.92663850m, -9.23m, 81, 1.25564805m, 67.57m, 78, 1.06878124m, 18.81m, "RESEARCHING"),
        R("CPA-FAILED_BREAKOUT|BASELINE|5m", "FAILED_BREAKOUT", "BASELINE", 4535, 0.78435497m, -3466.15m, 1272, 0.85758422m, -686.72m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-FAILED_BREAKOUT|CONTEXTUAL|5m", "FAILED_BREAKOUT", "CONTEXTUAL", 16, 0.38875179m, -45.91m, 35, 0.97110375m, -3.73m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-FAILED_BREAKOUT|STRICT|5m", "FAILED_BREAKOUT", "STRICT", 8, 0.55512106m, -15.63m, 13, 2.82034230m, 50.29m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-BREAKOUT_RETEST|BASELINE|5m", "BREAKOUT_RETEST", "BASELINE", 4915, 0.78717703m, -3657.88m, 1493, 0.62454445m, -2172.98m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-BREAKOUT_RETEST|CONTEXTUAL|5m", "BREAKOUT_RETEST", "CONTEXTUAL", 26, 0.70122070m, -31.69m, 61, 0.86724586m, -31.35m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-BREAKOUT_RETEST|STRICT|5m", "BREAKOUT_RETEST", "STRICT", 5, 0.43633011m, -12.59m, 16, 1.14342316m, 7.36m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-PULLBACK|BASELINE|5m", "PULLBACK", "BASELINE", 4128, 0.80737582m, -2819.71m, 1231, 0.83800120m, -760.46m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-PULLBACK|CONTEXTUAL|5m", "PULLBACK", "CONTEXTUAL", 24, 0.45303820m, -58.84m, 52, 1.11306041m, 20.84m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-PULLBACK|STRICT|5m", "PULLBACK", "STRICT", 7, 2.23189057m, 21.52m, 14, 2.89153117m, 57.42m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-WM|BASELINE|5m", "WM", "BASELINE", 4671, 0.84661545m, -2621.56m, 1379, 0.83949129m, -842.79m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-WM|CONTEXTUAL|5m", "WM", "CONTEXTUAL", 242, 0.60811060m, -393.83m, 514, 1.04402140m, 80.10m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-WM|STRICT|5m", "WM", "STRICT", 57, 0.71179031m, -67.13m, 115, 1.01576351m, 6.35m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-FLAG|BASELINE|5m", "FLAG", "BASELINE", 5528, 0.78114731m, -4133.05m, 1632, 0.60605780m, -2493.75m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-FLAG|CONTEXTUAL|5m", "FLAG", "CONTEXTUAL", 215, 0.66676062m, -288.28m, 521, 0.94589418m, -102.69m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-FLAG|STRICT|5m", "FLAG", "STRICT", 26, 0.62303803m, -41.12m, 43, 0.87051817m, -20.55m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-COMPRESSION|CONTINUATION|5m", "COMPRESSION", "CONTINUATION", 135, 1.06835534m, 32.99m, 35, 1.46873638m, 49.15m, 50, 1.08205821m, 14.08m, "RESEARCHING"),
        R("CPA-COMPRESSION|REVERSAL|5m", "COMPRESSION", "REVERSAL", 71, 1.14611563m, 36.10m, 19, 2.12552040m, 50.51m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-COMPRESSION|CONTINUATION_CONTEXT|5m", "COMPRESSION", "CONTINUATION_CONTEXT", 40, 0.79053825m, -33.61m, 11, 1.87533894m, 27.39m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-COMPRESSION|REVERSAL_CONTEXT|5m", "COMPRESSION", "REVERSAL_CONTEXT", 27, 1.11647432m, 11.20m, 4, 5.54164849m, 24.03m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-MTF|BASELINE|5m", "MTF", "BASELINE", 5435, 0.77436673m, -4191.30m, 1587, 0.72802892m, -1615.94m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-MTF|CONTEXTUAL|5m", "MTF", "CONTEXTUAL", 145, 0.54793215m, -278.33m, 406, 0.94137681m, -88.23m, 0, 0m, 0m, "VALIDATION_FAILED"),
        R("CPA-MTF|STRICT|5m", "MTF", "STRICT", 39, 0.62422591m, -59.16m, 76, 1.03306124m, 8.76m, 0, 0m, 0m, "VALIDATION_FAILED")
    ];

    public static IReadOnlyList<Row> SelectedRows { get; } = Phase8.Where(Selected).ToArray();

    public static string StrictFailure(Row row) => row.HypothesisId switch
    {
        "CPA-SWEEP|CONTEXTUAL|5m" => "Pre-OOS gate failed: in-sample profit factor 0.36 is below 0.90. Validation passed the near-miss screen only.",
        "CPA-SWEEP|STRICT|5m" => "Entered OOS but failed the interesting bar: walk-forward had 2 trades, cost stress at 2.0x profit factor was below 1, and one block held almost all of the sample.",
        "CPA-PULLBACK|CONTEXTUAL|5m" => "Pre-OOS gate failed: in-sample trades 24 are below 30. Validation passed the near-miss screen only.",
        "CPA-WM|CONTEXTUAL|5m" => "Pre-OOS gate failed: in-sample profit factor 0.61 is below 0.90. Validation passed the near-miss screen only.",
        "CPA-WM|STRICT|5m" => "Pre-OOS gate failed: in-sample profit factor 0.71 is below 0.90. Validation passed the near-miss screen only.",
        "CPA-COMPRESSION|CONTINUATION|5m" => "Entered OOS but is the family baseline, not a contextual improvement. Walk-forward had 2 trades. Short-side net was not positive.",
        "CPA-MTF|STRICT|5m" => "Pre-OOS gate failed: in-sample profit factor 0.62 is below 0.90. Validation passed the near-miss screen only.",
        _ => "Failed the frozen pre-OOS gate or the out-of-sample interesting bar. Not VALIDATED_FOR_PAPER."
    };

    private static Row R(
        string id,
        string family,
        string variant,
        int isN,
        decimal isPf,
        decimal isNet,
        int valN,
        decimal valPf,
        decimal valNet,
        int oosN,
        decimal oosPf,
        decimal oosNet,
        string status) =>
        new(id, family, variant, isN, isPf, isNet, valN, valPf, valNet, oosN, oosPf, oosNet, status);
}
