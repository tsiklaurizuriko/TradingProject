using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Infrastructure.Persistence;

namespace TradingPlatform.Infrastructure.Security;

public sealed class ExchangeCredentialStore : IExchangeCredentialStore
{
    private readonly TradingDbContext _db;
    private readonly ISecretProtector _protector;

    public ExchangeCredentialStore(TradingDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task StoreAsync(Guid exchangeAccountId, string apiKey, string apiSecret, CancellationToken cancellationToken = default)
    {
        var account = await _db.ExchangeAccounts
            .Include(a => a.Credential)
            .FirstAsync(a => a.Id == exchangeAccountId, cancellationToken);

        var keyBytes = Encoding.UTF8.GetBytes(_protector.Protect(apiKey.Trim()));
        var secretBytes = Encoding.UTF8.GetBytes(_protector.Protect(apiSecret.Trim()));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey.Trim())))[..12];

        if (account.Credential is null)
        {
            account.Credential = new ExchangeCredential
            {
                ExchangeAccountId = account.Id,
                ApiKeyCipher = keyBytes,
                ApiSecretCipher = secretBytes,
                Nonce = [],
                Tag = []
            };
            await _db.ExchangeCredentials.AddAsync(account.Credential, cancellationToken);
        }
        else
        {
            account.Credential.ApiKeyCipher = keyBytes;
            account.Credential.ApiSecretCipher = secretBytes;
        }

        account.LiveEnabled = true;
        account.CanTrade = true;
        account.IsTestnet = false;
        account.ApiKeyFingerprint = fingerprint;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(string ApiKey, string ApiSecret)?> GetAsync(Guid exchangeAccountId, CancellationToken cancellationToken = default)
    {
        var credential = await _db.ExchangeCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ExchangeAccountId == exchangeAccountId, cancellationToken);
        if (credential is null || credential.ApiKeyCipher.Length == 0)
        {
            return null;
        }

        var apiKey = _protector.Unprotect(Encoding.UTF8.GetString(credential.ApiKeyCipher));
        var apiSecret = _protector.Unprotect(Encoding.UTF8.GetString(credential.ApiSecretCipher));
        return (apiKey, apiSecret);
    }

    public Task<ExchangeAccount?> GetLiveAccountAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.ExchangeAccounts
            .Include(a => a.Credential)
            .FirstOrDefaultAsync(
                a => a.UserId == userId && a.Name == "Binance Live" && a.DeletedAt == null,
                cancellationToken);

    public async Task<ExchangeAccount> GetOrCreateLiveAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var existing = await GetLiveAccountAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var account = new ExchangeAccount
        {
            UserId = userId,
            Name = "Binance Live",
            Exchange = ExchangeType.BinanceSpot,
            IsTestnet = false,
            LiveEnabled = false,
            CanTrade = false,
            ApiKeyFingerprint = "none"
        };
        await _db.ExchangeAccounts.AddAsync(account, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return account;
    }
}
