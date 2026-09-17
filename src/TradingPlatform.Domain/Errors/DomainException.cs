namespace TradingPlatform.Domain.Errors;

public static class ErrorCodes
{
    public const string InternalError = "INTERNAL_ERROR";
    public const string ValidationFailed = "VALIDATION_FAILED";

    public const string BotNotFound = "BOT_NOT_FOUND";
    public const string BotAlreadyRunning = "BOT_ALREADY_RUNNING";
    public const string StrategyInvalid = "STRATEGY_INVALID";
    public const string RiskLimitExceeded = "RISK_LIMIT_EXCEEDED";
    public const string OrderRejected = "ORDER_REJECTED";
    public const string ExchangeUnavailable = "EXCHANGE_UNAVAILABLE";
    public const string ExchangeRateLimit = "EXCHANGE_RATE_LIMIT";
    public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
    public const string InvalidSymbol = "INVALID_SYMBOL";
    public const string InvalidQuantity = "INVALID_QUANTITY";
    public const string InvalidPrice = "INVALID_PRICE";
    public const string DuplicateOrder = "DUPLICATE_ORDER";
    public const string ReconciliationRequired = "RECONCILIATION_REQUIRED";
    public const string LiveTradingDisabled = "LIVE_TRADING_DISABLED";
    public const string KillSwitchActive = "KILL_SWITCH_ACTIVE";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
}

public sealed class DomainException : Exception
{
    public DomainException(
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object?>? Details { get; }
}
