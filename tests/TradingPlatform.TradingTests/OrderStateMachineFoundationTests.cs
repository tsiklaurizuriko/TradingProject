using TradingPlatform.Domain.Trading;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.TradingTests;

public sealed class OrderStateMachineFoundationTests
{
    [Fact]
    public void Order_status_includes_submitted_and_filled_as_distinct_states()
    {
        OrderStatus.Submitted.Should().NotBe(OrderStatus.Filled);
        OrderStatus.Submitting.Should().NotBe(OrderStatus.Filled);
    }
}
