using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.Infrastructure.Security;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class CredentialRekeyTests
{
    private static AesGcmSecretProtector Protector(string key) =>
        new(Options.Create(new CredentialEncryptionOptions { EncryptionKey = key }));

    [Fact]
    public async Task Placeholder_encrypted_secrets_move_to_the_configured_key()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase($"rekey-{Guid.NewGuid():N}")
            .Options;
        await using var db = new TradingDbContext(options);
        var old = Protector(TradingHostConfiguration.PlaceholderEncryptionKey);
        var user = new User
        {
            Email = "admin@localhost",
            NormalizedEmail = "ADMIN@LOCALHOST",
            DisplayName = "Admin",
            PasswordHash = "x",
            TwoFactorSecretEncrypted = old.Protect("totp-secret")
        };
        var account = new ExchangeAccount { User = user, UserId = user.Id, Name = "main" };
        account.Credential = new ExchangeCredential
        {
            ExchangeAccount = account,
            ApiKeyCipher = Encoding.UTF8.GetBytes(old.Protect("api-key")),
            ApiSecretCipher = Encoding.UTF8.GetBytes(old.Protect("api-secret"))
        };
        db.Users.Add(user);
        db.ExchangeAccounts.Add(account);
        await db.SaveChangesAsync();

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var moved = await CredentialRekey.RunAsync(db, key, NullLogger.Instance, CancellationToken.None);
        var again = await CredentialRekey.RunAsync(db, key, NullLogger.Instance, CancellationToken.None);

        moved.Should().Be(2);
        again.Should().Be(0);
        var current = Protector(key);
        var credential = await db.ExchangeCredentials.SingleAsync();
        current.Unprotect(Encoding.UTF8.GetString(credential.ApiKeyCipher)).Should().Be("api-key");
        current.Unprotect(Encoding.UTF8.GetString(credential.ApiSecretCipher)).Should().Be("api-secret");
        current.Unprotect(user.TwoFactorSecretEncrypted!).Should().Be("totp-secret");
    }
}
