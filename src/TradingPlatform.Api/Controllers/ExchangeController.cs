using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Api.Controllers;

[ApiController]
[Route("api/trading/exchange")]
[AllowAnonymous]
public sealed class ExchangeController : ControllerBase
{
    private readonly IExchangeAccountService _exchange;

    public ExchangeController(IExchangeAccountService exchange)
    {
        _exchange = exchange;
    }

    [HttpGet("status")]
    public Task<ExchangeConnectionDto> Status(CancellationToken cancellationToken) =>
        _exchange.GetStatusAsync(UserId(), cancellationToken);

    [HttpPost("credentials")]
    public Task<ExchangeConnectionDto> Save([FromBody] SaveExchangeCredentialsRequest request, CancellationToken cancellationToken) =>
        _exchange.SaveLiveKeysAsync(UserId(), request.ApiKey, request.ApiSecret, cancellationToken);

    private Guid UserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
