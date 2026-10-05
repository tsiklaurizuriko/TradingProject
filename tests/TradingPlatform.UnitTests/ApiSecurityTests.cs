using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using TradingPlatform.Api.Hosting;
using TradingPlatform.Application.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class ApiSecurityTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    [Fact]
    public void Placeholder_secrets_are_reported()
    {
        var problems = SecretsGuard.Problems(Config(
            ("Jwt:SigningKey", SecretsGuard.DefaultJwtKey),
            ("Credentials:EncryptionKey", SecretsGuard.DefaultEncryptionKey)));
        problems.Should().HaveCount(2);
    }

    [Fact]
    public void Short_or_missing_jwt_key_is_reported()
    {
        SecretsGuard.Problems(Config(("Jwt:SigningKey", "short"), ("Credentials:EncryptionKey", "x"))).Should().ContainSingle();
        SecretsGuard.Problems(Config(("Credentials:EncryptionKey", "x"))).Should().ContainSingle();
    }

    [Fact]
    public void Placeholders_are_fatal_outside_development()
    {
        var config = Config(("Jwt:SigningKey", SecretsGuard.DefaultJwtKey), ("Credentials:EncryptionKey", SecretsGuard.DefaultEncryptionKey));
        var act = () => SecretsGuard.Enforce(config, Env(Environments.Production), Serilog.Core.Logger.None);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Placeholders_are_fatal_in_development_when_live_is_on()
    {
        var config = Config(
            ("Jwt:SigningKey", SecretsGuard.DefaultJwtKey),
            ("Credentials:EncryptionKey", SecretsGuard.DefaultEncryptionKey),
            ("Trading:LiveTradingEnabled", "true"));
        var act = () => SecretsGuard.Enforce(config, Env(Environments.Development), Serilog.Core.Logger.None);
        act.Should().Throw<InvalidOperationException>().WithMessage("*LiveTradingEnabled*");
    }

    [Fact]
    public void Placeholders_only_warn_in_development_with_live_off()
    {
        var config = Config(("Jwt:SigningKey", SecretsGuard.DefaultJwtKey), ("Credentials:EncryptionKey", SecretsGuard.DefaultEncryptionKey));
        var act = () => SecretsGuard.Enforce(config, Env(Environments.Development), Serilog.Core.Logger.None);
        act.Should().NotThrow();
    }

    [Fact]
    public void Committed_admin_seed_password_is_fatal_outside_development()
    {
        var config = Config(
            ("Jwt:SigningKey", new string('k', 48)),
            ("Credentials:EncryptionKey", "real-key"),
            ("Seed:AdminPassword", TradingPlatform.Infrastructure.Persistence.DatabaseSeeder.DevelopmentAdminPassword));
        SecretsGuard.Problems(config).Should().ContainSingle().Which.Should().Contain("Seed:AdminPassword");
        var act = () => SecretsGuard.Enforce(config, Env("Shadow"), Serilog.Core.Logger.None);
        act.Should().Throw<InvalidOperationException>();

        var own = Config(("Jwt:SigningKey", new string('k', 48)), ("Credentials:EncryptionKey", "real-key"), ("Seed:AdminPassword", "a-real-operator-password"));
        SecretsGuard.Problems(own).Should().BeEmpty();
    }

    [Fact]
    public void Workers_refuse_live_with_placeholder_encryption_key()
    {
        var act = () => TradingHostConfiguration.RejectPlaceholderSecretsWhenLive(Config(
            ("Trading:LiveTradingEnabled", "true"),
            ("Credentials:EncryptionKey", TradingHostConfiguration.PlaceholderEncryptionKey)));
        act.Should().Throw<InvalidOperationException>();

        var off = () => TradingHostConfiguration.RejectPlaceholderSecretsWhenLive(Config(
            ("Trading:LiveTradingEnabled", "false"),
            ("Credentials:EncryptionKey", TradingHostConfiguration.PlaceholderEncryptionKey)));
        off.Should().NotThrow();
    }

    [Fact]
    public void Local_secrets_are_not_generated_when_live_is_off()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "secrets.json");
        LocalSecrets.Resolve(Config(("Secrets:Path", path), ("Jwt:SigningKey", SecretsGuard.DefaultJwtKey))).Should().BeEmpty();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Non_live_runs_reuse_the_stored_encryption_key()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "secrets.json");
        try
        {
            var live = LocalSecrets.Resolve(Config(
                ("Trading:LiveTradingEnabled", "true"),
                ("Secrets:Path", path),
                ("Credentials:EncryptionKey", SecretsGuard.DefaultEncryptionKey)));

            var development = LocalSecrets.Resolve(Config(
                ("Trading:LiveTradingEnabled", "false"),
                ("Secrets:Path", path),
                ("Credentials:EncryptionKey", SecretsGuard.DefaultEncryptionKey)));

            development.Should().ContainSingle();
            development["Credentials:EncryptionKey"].Should().Be(live["Credentials:EncryptionKey"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Live_placeholders_are_replaced_once_and_reused_on_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "secrets.json");
        var settings = new[]
        {
            ("Trading:LiveTradingEnabled", (string?)"true"),
            ("Secrets:Path", path),
            ("Jwt:SigningKey", SecretsGuard.DefaultJwtKey),
            ("Credentials:EncryptionKey", SecretsGuard.DefaultEncryptionKey),
            ("Seed:AdminPassword", LocalSecrets.PlaceholderAdminPassword)
        };
        try
        {
            var first = LocalSecrets.Resolve(Config(settings));
            var second = LocalSecrets.Resolve(Config(settings));

            first.Should().HaveCount(3);
            second.Should().BeEquivalentTo(first);
            Convert.FromBase64String(first["Credentials:EncryptionKey"]!).Should().HaveCount(32);
            var merged = new ConfigurationBuilder().AddConfiguration(Config(settings)).AddInMemoryCollection(first).Build();
            SecretsGuard.Problems(merged).Should().BeEmpty();
            TradingHostConfiguration.RejectPlaceholderSecretsWhenLive(merged);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Configured_secrets_are_kept_when_live_is_on()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "secrets.json");
        LocalSecrets.Resolve(Config(
            ("Trading:LiveTradingEnabled", "true"),
            ("Secrets:Path", path),
            ("Jwt:SigningKey", new string('k', 48)),
            ("Credentials:EncryptionKey", "real-key"),
            ("Seed:AdminPassword", "a-real-operator-password"))).Should().BeEmpty();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Committed_appsettings_keep_live_trading_off()
    {
        var root = FindRepoRoot();
        foreach (var path in new[] { "src/TradingPlatform.Api/appsettings.json", "src/TradingPlatform.Workers/appsettings.json" })
        {
            var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root, path)).Build();
            config.GetValue<bool>("Trading:LiveTradingEnabled").Should().BeFalse(path);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TradingPlatform.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
