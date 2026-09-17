using TradingPlatform.Application;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Trading;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class FoundationTests
{
    [Fact]
    public void Timeframe_maps_to_binance_interval_and_duration()
    {
        Timeframe.FiveMinutes.ToBinanceInterval().Should().Be("5m");
        Timeframe.FiveMinutes.ToDuration().Should().Be(TimeSpan.FromMinutes(5));
        TimeframeExtensions.TryParseInterval("1d", out var parsed).Should().BeTrue();
        parsed.Should().Be(Timeframe.OneDay);
    }

    [Fact]
    public void Live_trading_is_a_distinct_mode_from_paper_and_testnet()
    {
        Enum.GetValues<TradingMode>().Should().Equal(
            TradingMode.Paper,
            TradingMode.Testnet,
            TradingMode.Live);
    }

    [Fact]
    public void Domain_exception_preserves_error_code_without_secret_data()
    {
        var ex = new DomainException(ErrorCodes.LiveTradingDisabled, "Live trading is disabled.");
        ex.Code.Should().Be(ErrorCodes.LiveTradingDisabled);
        ex.Message.Should().NotContain("secret");
        ex.Message.Should().NotContain("apiKey");
    }

    [Fact]
    public void Application_composition_registers_without_throwing()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddApplication();
        services.Should().NotBeNull();
    }
}
