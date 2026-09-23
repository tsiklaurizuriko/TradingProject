using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TradingPlatform.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task Live_health_endpoint_returns_success()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Scalping_research_endpoint_is_read_only_and_live_off()
    {
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/api/trading/research/scalping");
        json.Should().Contain("LIVE = OFF");
        json.Should().Contain("validatedForPaperAssigned");
        json.Should().Contain("false");
        json.Should().Contain("scalp_ema_momentum");
    }

    [Fact]
    public async Task Price_action_research_endpoint_is_read_only_and_live_off()
    {
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/api/trading/research/scalping/price-action");
        json.Should().Contain("LIVE = OFF");
        json.Should().Contain("PRICE ACTION LIVE = OFF");
        json.Should().Contain("validatedForPaperAssigned");
        json.Should().Contain("false");
        json.Should().Contain("pa_w_double_bottom");
        json.Should().Contain("NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task System_health_reports_live_trading_disabled()
    {
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/api/system/health");
        json.Should().Contain("liveTradingEnabled");
        json.Should().Contain("false");
    }
}
