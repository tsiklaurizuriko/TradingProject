using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Trading;
using TradingPlatform.Infrastructure.Persistence;

namespace TradingPlatform.Infrastructure.Security;

/// <summary>
/// Rows written while the committed placeholder key was in use are re-encrypted with the configured key,
/// so moving to a real key does not lose stored Binance keys or two-factor secrets.
/// </summary>
public static class CredentialRekey
{
    public static async Task<int> RunAsync(TradingDbContext db, string encryptionKey, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(encryptionKey)
            || string.Equals(encryptionKey, TradingHostConfiguration.PlaceholderEncryptionKey, StringComparison.Ordinal))
        {
            return 0;
        }

        var current = Protector(encryptionKey);
        var placeholder = Protector(TradingHostConfiguration.PlaceholderEncryptionKey);
        var moved = 0;

        var credentials = await db.ExchangeCredentials.ToListAsync(cancellationToken);
        foreach (var credential in credentials)
        {
            var key = Rekey(Encoding.UTF8.GetString(credential.ApiKeyCipher), current, placeholder);
            var secret = Rekey(Encoding.UTF8.GetString(credential.ApiSecretCipher), current, placeholder);
            if (key is null && secret is null)
            {
                continue;
            }

            if (key is not null)
            {
                credential.ApiKeyCipher = Encoding.UTF8.GetBytes(key);
            }

            if (secret is not null)
            {
                credential.ApiSecretCipher = Encoding.UTF8.GetBytes(secret);
            }

            moved++;
        }

        var users = await db.Users.IgnoreQueryFilters()
            .Where(user => user.TwoFactorSecretEncrypted != null)
            .ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            var secret = Rekey(user.TwoFactorSecretEncrypted!, current, placeholder);
            if (secret is null)
            {
                continue;
            }

            user.TwoFactorSecretEncrypted = secret;
            moved++;
        }

        if (moved > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Re-encrypted {Count} stored secrets from the placeholder key to the configured Credentials:EncryptionKey.", moved);
        }

        return moved;
    }

    /// <summary>Null when the value already opens with the current key, or opens with neither key.</summary>
    private static string? Rekey(string cipher, AesGcmSecretProtector current, AesGcmSecretProtector placeholder)
    {
        if (string.IsNullOrEmpty(cipher) || Opens(current, cipher))
        {
            return null;
        }

        try
        {
            return current.Protect(placeholder.Unprotect(cipher));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static bool Opens(AesGcmSecretProtector protector, string cipher)
    {
        try
        {
            protector.Unprotect(cipher);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static AesGcmSecretProtector Protector(string key) =>
        new(Options.Create(new CredentialEncryptionOptions { EncryptionKey = key }));
}
