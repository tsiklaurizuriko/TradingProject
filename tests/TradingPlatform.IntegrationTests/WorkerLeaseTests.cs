using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Infrastructure.Persistence;
using Xunit;

namespace TradingPlatform.IntegrationTests;

public sealed class WorkerLeaseTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__TradingPlatform")
        ?? "Host=localhost;Port=5432;Database=TradingProject;Username=admin;Password=admin";

    [Fact]
    public async Task Only_one_process_holds_a_lease_and_the_other_takes_over_when_it_goes_away()
    {
        var name = "test-" + Guid.NewGuid().ToString("N");
        var first = new PostgresWorkerLease(ConnectionString, NullLogger<PostgresWorkerLease>.Instance);
        await using var second = new PostgresWorkerLease(ConnectionString, NullLogger<PostgresWorkerLease>.Instance);

        (await first.HoldAsync(name)).Should().BeTrue("Postgres must be reachable for this test");
        (await first.HoldAsync(name)).Should().BeTrue("the leader keeps the lease across cycles");
        (await second.HoldAsync(name)).Should().BeFalse("a second host must not run the same order loop");

        await first.DisposeAsync();

        (await second.HoldAsync(name)).Should().BeTrue("the lease is released when the leader's session ends");
    }

    [Fact]
    public async Task Different_loops_have_independent_leases()
    {
        await using var a = new PostgresWorkerLease(ConnectionString, NullLogger<PostgresWorkerLease>.Instance);
        await using var b = new PostgresWorkerLease(ConnectionString, NullLogger<PostgresWorkerLease>.Instance);
        var suffix = Guid.NewGuid().ToString("N");

        (await a.HoldAsync(WorkerLeaseNames.BotEngine + suffix)).Should().BeTrue();
        (await b.HoldAsync(WorkerLeaseNames.NewsTrading + suffix)).Should().BeTrue();
        PostgresWorkerLease.LockKey(WorkerLeaseNames.BotEngine).Should().NotBe(PostgresWorkerLease.LockKey(WorkerLeaseNames.NewsTrading));
        PostgresWorkerLease.LockKey(WorkerLeaseNames.BotEngine).Should().Be(PostgresWorkerLease.LockKey(WorkerLeaseNames.BotEngine));
    }

    [Fact]
    public async Task Unreachable_database_means_no_leadership_instead_of_an_exception()
    {
        await using var lease = new PostgresWorkerLease(
            "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2",
            NullLogger<PostgresWorkerLease>.Instance);

        (await lease.HoldAsync(WorkerLeaseNames.BotEngine)).Should().BeFalse();
    }
}
