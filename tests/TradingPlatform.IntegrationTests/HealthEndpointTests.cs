using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Domain.Identity;
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
        var client = Client(RoleNames.Trader);
        var json = await client.GetStringAsync("/api/trading/research/scalping");
        json.Should().Contain("LIVE = OFF");
        json.Should().Contain("validatedForPaperAssigned");
        json.Should().Contain("false");
        json.Should().Contain("scalp_ema_momentum");
    }

    [Fact]
    public async Task Price_action_research_endpoint_is_read_only_and_live_off()
    {
        var client = Client(RoleNames.Trader);
        var json = await client.GetStringAsync("/api/trading/research/scalping/price-action");
        json.Should().Contain("LIVE = OFF");
        json.Should().Contain("PRICE ACTION LIVE = OFF");
        json.Should().Contain("validatedForPaperAssigned");
        json.Should().Contain("false");
        json.Should().Contain("pa_w_double_bottom");
        json.Should().Contain("NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task System_health_reports_live_submission_disabled_by_default()
    {
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/api/system/health");
        json.Should().Contain("\"liveTradingEnabled\":false");
    }

    [Theory]
    [InlineData("GET", "/api/trading/overview")]
    [InlineData("GET", "/api/trading/research/news")]
    [InlineData("GET", "/api/trading/exchange/status")]
    [InlineData("POST", "/api/trading/emergency-stop")]
    [InlineData("POST", "/api/trading/flatten-all")]
    [InlineData("POST", "/api/trading/research/news/start")]
    [InlineData("POST", "/api/trading/exchange/credentials")]
    [InlineData("POST", "/api/strategies/cross-sectional-reversal/cross_sectional_reversal_return_15m/live/enable")]
    [InlineData("POST", "/hubs/trading/negotiate?negotiateVersion=1")]
    public async Task Anonymous_requests_to_trading_surface_are_rejected(string method, string path)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/trading/emergency-stop")]
    [InlineData("/api/trading/flatten-all")]
    [InlineData("/api/trading/research/news/start")]
    [InlineData("/api/trading/exchange/credentials")]
    [InlineData("/api/strategies/cross-sectional-reversal/cross_sectional_reversal_return_15m/live/enable")]
    [InlineData("/api/trading/bots/start-all")]
    public async Task Non_operator_cannot_mutate(string path)
    {
        var client = Client(RoleNames.Trader);
        var response = await client.PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Operator_can_disable_cross_sectional_live_and_it_actually_turns_off()
    {
        var client = Client(RoleNames.Admin);
        var response = await client.PostAsync(
            "/api/strategies/cross-sectional-reversal/cross_sectional_reversal/live/disable",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"liveStatus\":\"OFF\"");
    }

    [Fact]
    public async Task Self_registration_is_disabled_by_default()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            "/api/auth/register",
            new StringContent("{\"email\":\"x@example.com\",\"password\":\"Abcdef123!xyz\",\"displayName\":\"x\"}", System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Email = $"{role.ToLowerInvariant()}@test.local", DisplayName = role };
        var token = tokens.CreateAccessToken(user, [role], []);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
