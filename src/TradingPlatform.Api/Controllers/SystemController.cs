using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;
    private readonly IConfiguration _configuration;

    public SystemController(HealthCheckService healthCheckService, IConfiguration configuration)
    {
        _healthCheckService = healthCheckService;
        _configuration = configuration;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckHealthAsync(cancellationToken);
        var liveEnabled = _configuration.GetValue("Trading:LiveTradingEnabled", false);
        return Ok(new
        {
            status = report.Status.ToString(),
            liveTradingEnabled = liveEnabled,
            defaultMode = _configuration["Trading:DefaultMode"] ?? TradingMode.Paper.ToString(),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), e.Value.Description })
        });
    }
}
