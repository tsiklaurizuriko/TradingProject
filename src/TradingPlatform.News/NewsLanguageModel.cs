using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace TradingPlatform.News;

public sealed record NewsModelRequest(string Model, string SystemPrompt, string UserPrompt, TimeSpan Timeout);

public sealed record NewsModelCompletion(bool Succeeded, string? Content, string? Error, bool TimedOut, string Provider, string Model);

public interface INewsLanguageModel
{
    Task<NewsModelCompletion> CompleteAsync(NewsModelRequest request, CancellationToken cancellationToken);
}

/// <summary>OpenAI-compatible chat completions. The vendor is whatever base URL and model the configuration names.</summary>
public sealed class OpenAiCompatibleLanguageModel : INewsLanguageModel
{
    private readonly HttpClient _http;
    private readonly Func<NewsOptions> _options;
    private readonly ILogger _logger;

    public OpenAiCompatibleLanguageModel(IHttpClientFactory factory, IOptionsMonitor<NewsOptions> options, ILogger<OpenAiCompatibleLanguageModel> logger)
        : this(factory.CreateClient("news-ai"), () => options.CurrentValue, logger)
    {
    }

    public OpenAiCompatibleLanguageModel(HttpClient http, NewsOptions options, ILogger? logger = null)
        : this(http, () => options, logger ?? NullLogger.Instance)
    {
    }

    private OpenAiCompatibleLanguageModel(HttpClient http, Func<NewsOptions> options, ILogger logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<NewsModelCompletion> CompleteAsync(NewsModelRequest request, CancellationToken cancellationToken)
    {
        var options = _options();
        var provider = string.IsNullOrWhiteSpace(options.Ai.Provider) ? "openai-compatible" : options.Ai.Provider;
        if (!options.Ai.HasKey())
        {
            return new NewsModelCompletion(false, null, "News AI is enabled but News:Ai:ApiKey is empty.", false, provider, request.Model);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(20) : request.Timeout);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var message = Build(options, request);
                using var response = await _http.SendAsync(message, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    if (attempt == 1 && (status == 429 || status >= 500))
                    {
                        continue;
                    }

                    return new NewsModelCompletion(false, null, "Model HTTP " + status + ".", false, provider, request.Model);
                }

                var content = ReadContent(body);
                return string.IsNullOrWhiteSpace(content)
                    ? new NewsModelCompletion(false, null, "Model response had no content.", false, provider, request.Model)
                    : new NewsModelCompletion(true, content, null, false, provider, request.Model);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new NewsModelCompletion(false, null, "The model timed out.", true, provider, request.Model);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                _logger.LogWarning(ex, "NewsAiFailed {Model} {Reason}", request.Model, ex.Message);
                if (attempt == 1)
                {
                    continue;
                }

                return new NewsModelCompletion(false, null, "The model call failed.", false, provider, request.Model);
            }
        }

        return new NewsModelCompletion(false, null, "The model call failed.", false, provider, request.Model);
    }

    private static HttpRequestMessage Build(NewsOptions options, NewsModelRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 1800,
            ["messages"] = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            }
        };
        if (options.Ai.UseJsonResponseFormat)
        {
            payload["response_format"] = new { type = "json_object" };
        }

        var message = new HttpRequestMessage(HttpMethod.Post, ChatUrl(options.Ai.BaseUrl))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Ai.ApiKey.Trim());
        return message;
    }

    public static string ChatUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed + "/chat/completions";
    }

    public static string? ReadContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0].GetProperty("message");
        return message.TryGetProperty("content", out var content) ? content.GetString() : null;
    }
}
