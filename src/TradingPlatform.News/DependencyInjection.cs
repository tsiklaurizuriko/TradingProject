using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace TradingPlatform.News;

public static class NewsServiceCollectionExtensions
{
    public static IServiceCollection AddNews(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NewsOptions>(configuration.GetSection(NewsOptions.SectionName));
        services.AddHttpClient("news-ai", client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<INewsLanguageModel, OpenAiCompatibleLanguageModel>();
        return services;
    }
}
