namespace TradingPlatform.Domain.Strategies;

public static class StrategyValidationStatuses
{
    public const string ValidationPending = "VALIDATION_PENDING";
    public const string InsufficientData = "INSUFFICIENT_DATA";
    public const string ImplementationError = "IMPLEMENTATION_ERROR";
    public const string Unstable = "UNSTABLE";
    public const string OosDegradation = "OOS_DEGRADATION";
    public const string ValidatedForPaper = "VALIDATED_FOR_PAPER";
    public const string RejectedByTests = "REJECTED_BY_TESTS";
    public const string Researching = "RESEARCHING";
    public const string DataUnavailable = "DATA_UNAVAILABLE";
}
