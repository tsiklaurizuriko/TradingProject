using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TradingPlatform.Binance;

public static class BinanceHmac
{
    public static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string FormatDecimal(decimal value)
    {
        var text = value.ToString("0.########", CultureInfo.InvariantCulture);
        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text.Length == 0 ? "0" : text;
    }
}
