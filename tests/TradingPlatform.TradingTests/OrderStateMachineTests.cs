using TradingPlatform.Domain.Trading;
using TradingPlatform.Execution;
using FluentAssertions;
using Xunit;

namespace TradingPlatform.TradingTests;

public sealed class OrderStateMachineTests
{
    [Fact]
    public void Submitted_is_not_filled()
    {
        OrderStatus.Submitted.Should().NotBe(OrderStatus.Filled);
        OrderStateMachine.CanTransition(OrderStatus.Submitting, OrderStatus.Submitted).Should().BeTrue();
        OrderStateMachine.CanTransition(OrderStatus.Submitted, OrderStatus.Filled).Should().BeTrue();
        OrderStateMachine.CanTransition(OrderStatus.New, OrderStatus.Filled).Should().BeFalse();
    }

    [Fact]
    public void Terminal_states_do_not_accept_further_transitions()
    {
        OrderStateMachine.CanTransition(OrderStatus.Filled, OrderStatus.Cancelled).Should().BeFalse();
        OrderStateMachine.CanTransition(OrderStatus.Rejected, OrderStatus.Submitted).Should().BeFalse();
    }
}
