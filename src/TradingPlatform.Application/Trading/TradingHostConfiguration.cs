using Microsoft.Extensions.Configuration;

namespace TradingPlatform.Application.Trading;

public static class TradingHostConfiguration
{
    public static void RejectUnsupportedMode(IConfiguration configuration)
    {
        var mode = configuration["Trading:DefaultMode"];
        if (!string.IsNullOrWhiteSpace(mode)
            && !string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Trading:DefaultMode is '{mode}'. Only Live is supported. This process will not start, and the value was not treated as live.");
        }
    }
}
