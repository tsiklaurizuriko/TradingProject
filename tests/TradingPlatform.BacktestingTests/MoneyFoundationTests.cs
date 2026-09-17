using TradingPlatform.Domain.Common;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.BacktestingTests;

public sealed class MoneyFoundationTests
{
    [Fact]
    public void Money_uses_decimal_addition()
    {
        var result = new Money(1.10m) + new Money(2.20m);
        result.Amount.Should().Be(3.30m);
    }
}
