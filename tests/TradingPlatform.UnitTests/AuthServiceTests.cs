using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Auth;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.Infrastructure.Security;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task Register_then_login_issues_tokens_and_hashes_password()
    {
        await using var db = CreateDb();
        var jwtOptions = new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            SigningKey = "unit-test-signing-key-which-is-long-enough",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        };
        var hasher = new BcryptPasswordHasher();
        var users = new UserRepository(db);
        db.Roles.Add(new Role { Name = RoleNames.Trader, Description = "Trader" });
        await db.SaveChangesAsync();

        var sut = new AuthService(
            users,
            new RoleRepository(db),
            new RefreshTokenRepository(db),
            new PasswordResetTokenRepository(db),
            hasher,
            new Sha256TokenHasher(),
            new JwtTokenService(Options.Create(jwtOptions)),
            new TotpService(),
            new AesGcmSecretProtector(Options.Create(new CredentialEncryptionOptions
            {
                EncryptionKey = Convert.ToBase64String(new byte[32])
            })),
            db,
            new AuditService(db),
            new SystemClock(),
            new CorrelationIdAccessor(),
            jwtOptions);

        var registered = await sut.RegisterAsync(new RegisterRequest("trader@localhost", "StrongPass1x", "Trader"), "127.0.0.1");
        registered.AccessToken.Should().NotBeNullOrWhiteSpace();
        registered.Roles.Should().Contain(RoleNames.Trader);

        var login = await sut.LoginAsync(new LoginRequest("trader@localhost", "StrongPass1x", null), "127.0.0.1");
        login.RefreshToken.Should().NotBe(registered.RefreshToken);

        var stored = await db.Users.AsNoTracking().SingleAsync();
        stored.PasswordHash.Should().NotBe("StrongPass1x");
        stored.PasswordHash.Should().StartWith("$2");
    }

    [Fact]
    public void Secret_protector_round_trips_without_logging_plaintext()
    {
        var protector = new AesGcmSecretProtector(Options.Create(new CredentialEncryptionOptions
        {
            EncryptionKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
        }));
        var protectedValue = protector.Protect("binance-secret");
        protectedValue.Should().NotContain("binance-secret");
        protector.Unprotect(protectedValue).Should().Be("binance-secret");
    }

    private static TradingDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TradingDbContext(options);
    }
}
