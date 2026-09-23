namespace TradingPlatform.Strategies.PriceAction;

public static class PatternKinds
{
    public const string Doji = "DOJI";
    public const string Hammer = "HAMMER";
    public const string InvertedHammer = "INVERTED_HAMMER";
    public const string ShootingStar = "SHOOTING_STAR";
    public const string HangingMan = "HANGING_MAN";
    public const string BullishEngulfing = "BULLISH_ENGULFING";
    public const string BearishEngulfing = "BEARISH_ENGULFING";
    public const string InsideBar = "INSIDE_BAR";
    public const string OutsideBar = "OUTSIDE_BAR";
    public const string PinBar = "PIN_BAR";
    public const string Marubozu = "MARUBOZU";
    public const string ThreeBarReversal = "THREE_BAR_REVERSAL";
    public const string StrongImpulse = "STRONG_IMPULSE";
    public const string FailedContinuation = "FAILED_CONTINUATION";
    public const string UpperWickRejection = "UPPER_WICK_REJECTION";
    public const string LowerWickRejection = "LOWER_WICK_REJECTION";
    public const string BullishRejection = "BULLISH_REJECTION";
    public const string BearishRejection = "BEARISH_REJECTION";

    public const string WDoubleBottom = "W_DOUBLE_BOTTOM";
    public const string MDoubleTop = "M_DOUBLE_TOP";
    public const string BullFlag = "BULL_FLAG";
    public const string BearFlag = "BEAR_FLAG";
    public const string Pennant = "PENNANT";
    public const string AscendingTriangle = "ASCENDING_TRIANGLE";
    public const string DescendingTriangle = "DESCENDING_TRIANGLE";
    public const string SymmetricalTriangle = "SYMMETRICAL_TRIANGLE";
    public const string RisingWedge = "RISING_WEDGE";
    public const string FallingWedge = "FALLING_WEDGE";
    public const string Rectangle = "RECTANGLE";
    public const string HeadShoulders = "HEAD_SHOULDERS";
    public const string InverseHeadShoulders = "INVERSE_HEAD_SHOULDERS";
    public const string CupHandle = "CUP_AND_HANDLE";
    public const string BreakoutRetest = "BREAKOUT_RETEST";
    public const string FailedBreakout = "FAILED_BREAKOUT";
    public const string LiquiditySweepHigh = "LIQUIDITY_SWEEP_HIGH";
    public const string LiquiditySweepLow = "LIQUIDITY_SWEEP_LOW";
    public const string StructureBos = "STRUCTURE_BOS";
    public const string StructureChoch = "STRUCTURE_CHOCH";

    public const string NotImplemented = "NOT_IMPLEMENTED";
    public const string Detected = "DETECTED";
    public const string Confirmed = "CONFIRMED";
    public const string Failed = "FAILED";
    public const string Forming = "FORMING";
}

public readonly record struct SwingPoint(int PivotIndex, int ConfirmationIndex, decimal Price, bool IsHigh);

public sealed record PatternPoint(int Index, decimal Price, string Role);

public sealed record PatternOccurrence(
    string PatternType,
    string Version,
    int StartIndex,
    int DetectionIndex,
    int? ConfirmationIndex,
    int? EntryTriggerIndex,
    decimal? Neckline,
    decimal? Level,
    string Direction,
    string Status,
    IReadOnlyList<PatternPoint> Points,
    string? Notes = null);

public sealed record StructureBar(
    int Index,
    bool Hh,
    bool Hl,
    bool Lh,
    bool Ll,
    int Bias,
    bool BosBull,
    bool BosBear,
    bool ChochBull,
    bool ChochBear,
    bool Range,
    bool RangeContraction,
    bool RangeExpansion);

public sealed record SequenceBar(
    int Index,
    int BullRun,
    int BearRun,
    int CompressionRun,
    int ExpansionRun,
    bool Alternating,
    bool FailedContinuation,
    bool ImpulseFollowThrough);
