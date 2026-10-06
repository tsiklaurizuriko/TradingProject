namespace TradingPlatform.News;

public sealed class NewsPollSchedule
{
    private readonly Dictionary<string, DateTimeOffset> _lastAttempt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _nextEligible = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void Remember(string provider, DateTimeOffset lastAttemptUtc)
    {
        lock (_gate)
        {
            _lastAttempt[provider] = lastAttemptUtc;
        }
    }

    public bool TryBegin(string provider, DateTimeOffset now, TimeSpan interval)
    {
        lock (_gate)
        {
            if (_inFlight.Contains(provider))
            {
                return false;
            }

            if (_nextEligible.TryGetValue(provider, out var next) && now < next)
            {
                return false;
            }

            if (_lastAttempt.TryGetValue(provider, out var last) && now - last < interval)
            {
                return false;
            }

            _inFlight.Add(provider);
            return true;
        }
    }

    public void DelayUntil(string provider, DateTimeOffset nextEligibleUtc)
    {
        lock (_gate)
        {
            _nextEligible[provider] = nextEligibleUtc;
        }
    }

    public void Complete(string provider, DateTimeOffset attemptedAtUtc)
    {
        lock (_gate)
        {
            _inFlight.Remove(provider);
            _lastAttempt[provider] = attemptedAtUtc;
        }
    }

    public TimeSpan TimeUntilNext(DateTimeOffset now, IEnumerable<(string Provider, TimeSpan Interval)> providers)
    {
        lock (_gate)
        {
            var soonest = TimeSpan.FromMinutes(5);
            foreach (var (provider, interval) in providers)
            {
                if (_inFlight.Contains(provider))
                {
                    continue;
                }

                var remaining = _nextEligible.TryGetValue(provider, out var next) && next > now
                    ? next - now
                    : _lastAttempt.TryGetValue(provider, out var last)
                        ? interval - (now - last)
                        : TimeSpan.Zero;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }

                if (remaining < soonest)
                {
                    soonest = remaining;
                }
            }

            return soonest;
        }
    }
}

public static class NewsProviderCatalog
{
    public static IReadOnlyList<string> DefaultProviders { get; } =
        ["binance", "official-blogs", "rss"];

    public static IReadOnlyList<string> CredentialProviders { get; } =
        ["coingecko", "coindesk", "cryptopanic", "fred"];

    public const string CredentialsMissing = "Disabled: credentials not configured.";

    public static int PollIntervalMinutes(NewsOptions options, string provider)
    {
        var configured = 0;
        foreach (var pair in options.ProviderPollMinutes)
        {
            if (pair.Key.Equals(provider, StringComparison.OrdinalIgnoreCase) && pair.Value > 0)
            {
                configured = pair.Value;
                break;
            }
        }

        var minutes = configured > 0 ? configured : provider.ToLowerInvariant() switch
        {
            "gdelt" => 5,
            "binance" => 1,
            "official-blogs" => 5,
            "rss" => 5,
            "coingecko" => 5,
            "coindesk" => 5,
            "cryptopanic" => 5,
            "fred" => 5,
            _ => Math.Max(1, options.PollMinutes)
        };
        if (provider.Equals("gdelt", StringComparison.OrdinalIgnoreCase) && minutes < 5)
        {
            minutes = 5;
        }

        return Math.Max(1, minutes);
    }

    public static IReadOnlyList<INewsProvider> Create(NewsOptions options, HttpClient http)
    {
        var names = options.Providers.Length == 0 ? DefaultProviders : options.Providers;
        var providers = new List<INewsProvider>();
        foreach (var name in names)
        {
            if (IsCredentialProvider(name) && !HasCredentials(options, name))
            {
                continue;
            }

            if (name.Equals("gdelt", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new GdeltNewsProvider(http));
            }
            else if (name.Equals("coingecko", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new CoinGeckoNewsProvider(http, options));
            }
            else if (name.Equals("coindesk", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new CoinDeskNewsProvider(http, options));
            }
            else if (name.Equals("cryptopanic", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new CryptoPanicNewsProvider(http, options));
            }
            else if (name.Equals("fred", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new FredMacroReleaseProvider(http, options));
            }
            else if (name.Equals("rss", StringComparison.OrdinalIgnoreCase))
            {
                AddFeeds(providers, http, "rss", Feeds(options.RssFeeds, RssNewsProvider.DefaultPublisherFeeds));
            }
            else if (name.Equals("official-blogs", StringComparison.OrdinalIgnoreCase))
            {
                AddFeeds(providers, http, "official-blogs", Feeds(options.OfficialFeeds, RssNewsProvider.DefaultOfficialFeeds));
            }
            else if (name.Equals("binance", StringComparison.OrdinalIgnoreCase))
            {
                providers.Add(new BinanceAnnouncementProvider(http));
            }
            else
            {
                providers.Add(new UnknownNewsProvider(name));
            }
        }

        return providers;
    }

    public static IReadOnlyList<(string Name, string ScheduleKey)> ScheduledSources(NewsOptions options)
    {
        using var http = new HttpClient();
        return Create(options, http)
            .Select(provider => (provider.Name, provider.ScheduleKey))
            .ToList();
    }

    public static bool IsCredentialProvider(string name) =>
        CredentialProviders.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool HasCredentials(NewsOptions options, string name)
    {
        if (name.Equals("coingecko", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(options.CoinGeckoApiKey);
        }

        if (name.Equals("coindesk", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(options.CoinDeskApiKey);
        }

        if (name.Equals("cryptopanic", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(options.CryptoPanicToken);
        }

        if (name.Equals("fred", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(options.FredApiKey);
        }

        return false;
    }

    public static IReadOnlyList<NewsProviderReport> DisabledCredentialReports(NewsOptions options, DateTimeOffset now)
    {
        var active = options.Providers.Length == 0 ? DefaultProviders : options.Providers;
        var reports = new List<NewsProviderReport>();
        foreach (var name in CredentialProviders)
        {
            var listed = active.Contains(name, StringComparer.OrdinalIgnoreCase);
            var ready = HasCredentials(options, name);
            if (listed && ready)
            {
                continue;
            }

            var message = ready
                ? "Disabled: optional paid source is not enabled."
                : CredentialsMissing;
            reports.Add(new NewsProviderReport(name, false, message, 0, 0, now, false, null));
        }

        return reports;
    }

    private static IReadOnlyList<(string Publisher, string Url)> Feeds(
        IReadOnlyList<NewsFeedOption> configured,
        IReadOnlyList<(string Publisher, string Url)> fallback)
    {
        var feeds = configured
            .Where(feed => !string.IsNullOrWhiteSpace(feed.Url))
            .Select(feed => (string.IsNullOrWhiteSpace(feed.Publisher) ? feed.Url : feed.Publisher, feed.Url))
            .ToList();
        return feeds.Count == 0 ? fallback : feeds;
    }

    private static void AddFeeds(
        List<INewsProvider> providers,
        HttpClient http,
        string scheduleKey,
        IReadOnlyList<(string Publisher, string Url)> feeds)
    {
        var seen = new HashSet<string>(providers.Select(provider => provider.Name), StringComparer.Ordinal);
        foreach (var feed in feeds)
        {
            var name = feed.Publisher.Trim();
            if (name.Length > 64)
            {
                name = name[..64];
            }

            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                continue;
            }

            providers.Add(new RssNewsProvider(http, name, [feed], scheduleKey));
        }
    }

    private sealed class UnknownNewsProvider : INewsProvider
    {
        public UnknownNewsProvider(string name) => Name = name;

        public string Name { get; }

        public Task<NewsProviderBatch> FetchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            throw new NewsProviderException("Unknown news provider " + Name + ".");
    }
}

public static class NewsBackoff
{
    public static bool IsRateLimited(string? error) =>
        error is not null
        && (error.Contains("429", StringComparison.Ordinal)
            || error.Contains("rate limit", StringComparison.OrdinalIgnoreCase));

    public static TimeSpan Delay(string provider, TimeSpan interval)
    {
        var doubled = TimeSpan.FromTicks(Math.Max(interval.Ticks, TimeSpan.TicksPerMinute) * 2);
        var floor = provider.Equals("gdelt", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromMinutes(30)
            : TimeSpan.FromMinutes(10);
        return doubled > floor ? doubled : floor;
    }
}
