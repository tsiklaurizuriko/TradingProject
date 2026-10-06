using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace TradingPlatform.Application.Trading;

/// <summary>
/// With live trading on, secrets left at their committed placeholders are generated once and kept in a per-user file
/// shared by the API and Workers. The file is outside the repo and the database: the encryption key must not sit next
/// to the Binance keys it protects. Values set through configuration or environment variables always win.
/// </summary>
public static class LocalSecrets
{
    public const string JwtKey = "Jwt:SigningKey";
    public const string EncryptionKey = "Credentials:EncryptionKey";
    public const string AdminPasswordKey = "Seed:AdminPassword";
    public const string NewsAiApiKey = "News:Ai:ApiKey";

    public const string PlaceholderJwtKey = "CHANGE-ME-to-a-long-random-signing-key-32chars-min";
    public const string PlaceholderAdminPassword = "ChangeMe_Admin_123!";

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingPlatform", "secrets.json");

    /// <summary>Returns the values to layer over configuration. Empty when live trading is off or nothing is a placeholder.</summary>
    public static IReadOnlyDictionary<string, string?> Resolve(IConfiguration configuration)
    {
        var overlay = new Dictionary<string, string?>(ResolvePlaceholders(configuration), StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(configuration[NewsAiApiKey]))
        {
            var stored = ReadStored(PathFrom(configuration), NewsAiApiKey);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                overlay[NewsAiApiKey] = stored;
            }
        }

        return overlay;
    }

    private static IReadOnlyDictionary<string, string?> ResolvePlaceholders(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Trading:LiveTradingEnabled"))
        {
            return IsPlaceholder(configuration[EncryptionKey], TradingHostConfiguration.PlaceholderEncryptionKey)
                ? ReadExisting(PathFrom(configuration), EncryptionKey)
                : new Dictionary<string, string?>();
        }

        var missing = new List<string>();
        if (IsPlaceholder(configuration[JwtKey], PlaceholderJwtKey))
        {
            missing.Add(JwtKey);
        }

        if (IsPlaceholder(configuration[EncryptionKey], TradingHostConfiguration.PlaceholderEncryptionKey))
        {
            missing.Add(EncryptionKey);
        }

        if (IsPlaceholder(configuration[AdminPasswordKey], PlaceholderAdminPassword))
        {
            missing.Add(AdminPasswordKey);
        }

        if (missing.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return Load(PathFrom(configuration), missing);
    }

    private static string PathFrom(IConfiguration configuration)
    {
        var path = configuration["Secrets:Path"];
        return string.IsNullOrWhiteSpace(path) ? DefaultPath() : path;
    }

    /// <summary>
    /// Non-live runs reuse a key a live run already generated, so Binance keys stored under it still decrypt.
    /// Nothing is generated and no file is created.
    /// </summary>
    private static IReadOnlyDictionary<string, string?> ReadExisting(string path, string key)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string?>();
        }

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var stored = Read(file);
            return stored.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? new Dictionary<string, string?> { [key] = value }
                : new Dictionary<string, string?>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string?>();
        }
    }

    private static string? ReadStored(string path, string key)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var stored = Read(file);
            return stored.TryGetValue(key, out var value) ? value : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyDictionary<string, string?> Load(string path, IReadOnlyList<string> keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = OpenExclusive(path);
        var stored = Read(file);
        var changed = false;
        foreach (var key in keys)
        {
            if (!stored.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                stored[key] = Generate(key);
                changed = true;
            }
        }

        if (changed)
        {
            file.SetLength(0);
            JsonSerializer.Serialize(file, stored, new JsonSerializerOptions { WriteIndented = true });
            file.Flush(flushToDisk: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        return keys.ToDictionary(key => key, key => (string?)stored[key]);
    }

    private static bool IsPlaceholder(string? value, string placeholder) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, placeholder, StringComparison.Ordinal);

    private static string Generate(string key) => key switch
    {
        EncryptionKey => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        _ => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
    };

    private static Dictionary<string, string> Read(FileStream file)
    {
        if (file.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(file);
        file.Position = 0;
        return parsed is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
    }

    /// <summary>The API and Workers may start together; the exclusive handle keeps them from generating different keys.</summary>
    private static FileStream OpenExclusive(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 50)
            {
                Thread.Sleep(100);
            }
        }
    }
}
