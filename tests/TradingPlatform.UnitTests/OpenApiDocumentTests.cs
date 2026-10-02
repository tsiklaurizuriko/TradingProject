using FluentAssertions;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TradingPlatform.UnitTests;

public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task OpenApi_document_is_generated()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(TradingPlatform.Api.Controllers.TradingController).Assembly.GetName().Name
        });
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(TradingPlatform.Api.Controllers.TradingController).Assembly);
        builder.Services.AddOpenApi();

        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient();
            var response = await client.GetAsync($"{app.Urls.First()}/openapi/v1.json");
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body.Length > 1500 ? body[..1500] : body);
            body.Should().Contain("\"openapi\"");
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
