using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TradingPlatform.Infrastructure.Persistence;

public sealed class TradingDbContextFactory : IDesignTimeDbContextFactory<TradingDbContext>
{
    public TradingDbContext CreateDbContext(string[] args)
    {
        var apiPath = FindApiPath();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiPath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseNpgsql(configuration.GetConnectionString("TradingPlatform")
                       ?? "Host=localhost;Port=5432;Database=TradingProject;Username=admin;Password=admin")
            .Options;
        return new TradingDbContext(options);
    }

    private static string FindApiPath()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "src", "TradingPlatform.Api"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "TradingPlatform.Api"),
            Path.Combine(Directory.GetCurrentDirectory())
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? Directory.GetCurrentDirectory();
    }
}
