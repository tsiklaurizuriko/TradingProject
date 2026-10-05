using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using TradingPlatform.Domain.Identity;

namespace TradingPlatform.Api.Hosting;

public static class ApiPolicies
{
    public const string Operator = "Operator";
}

/// <summary>
/// Every action that is not a read (GET/HEAD) requires the Operator policy unless the action or its
/// controller is explicitly anonymous. New mutating endpoints are covered without remembering an attribute.
/// </summary>
public sealed class OperatorMutationsConvention : IControllerModelConvention
{
    private static readonly HashSet<string> ReadMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    public void Apply(ControllerModel controller)
    {
        if (controller.Attributes.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        foreach (var action in controller.Actions)
        {
            if (action.Attributes.OfType<IAllowAnonymous>().Any() || !IsMutation(action))
            {
                continue;
            }

            action.Filters.Add(new AuthorizeFilter([new AuthorizeAttribute(ApiPolicies.Operator)]));
        }
    }

    public static bool IsMutation(ActionModel action)
    {
        var methods = action.Selectors
            .SelectMany(s => s.EndpointMetadata.OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>())
            .SelectMany(m => m.HttpMethods)
            .ToList();
        if (methods.Count == 0)
        {
            methods = action.Attributes
                .OfType<Microsoft.AspNetCore.Mvc.Routing.IActionHttpMethodProvider>()
                .SelectMany(p => p.HttpMethods)
                .ToList();
        }

        return methods.Count == 0 || methods.Any(m => !ReadMethods.Contains(m));
    }
}

public static class SecretsGuard
{
    public const string DefaultJwtKey = TradingPlatform.Application.Trading.LocalSecrets.PlaceholderJwtKey;
    public const string DefaultEncryptionKey = TradingPlatform.Application.Trading.TradingHostConfiguration.PlaceholderEncryptionKey;

    /// <summary>
    /// Returns problems with configured secrets. Outside Development any problem is fatal.
    /// With live trading on, a default secret is fatal in every environment.
    /// </summary>
    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        var problems = new List<string>();
        var jwt = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            problems.Add("Jwt:SigningKey is missing.");
        }
        else if (System.Text.Encoding.UTF8.GetByteCount(jwt) < 32)
        {
            problems.Add("Jwt:SigningKey must be at least 32 bytes.");
        }
        else if (string.Equals(jwt, DefaultJwtKey, StringComparison.Ordinal))
        {
            problems.Add("Jwt:SigningKey is the committed placeholder.");
        }

        var encryption = configuration["Credentials:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(encryption))
        {
            problems.Add("Credentials:EncryptionKey is missing.");
        }
        else if (string.Equals(encryption, DefaultEncryptionKey, StringComparison.Ordinal))
        {
            problems.Add("Credentials:EncryptionKey is the committed placeholder.");
        }

        if (string.Equals(configuration["Seed:AdminPassword"], TradingPlatform.Infrastructure.Persistence.DatabaseSeeder.DevelopmentAdminPassword, StringComparison.Ordinal))
        {
            problems.Add("Seed:AdminPassword is the committed placeholder.");
        }

        return problems;
    }

    public static void Enforce(IConfiguration configuration, IHostEnvironment environment, Serilog.ILogger log)
    {
        var problems = Problems(configuration);
        if (problems.Count == 0)
        {
            return;
        }

        var live = configuration.GetValue<bool>("Trading:LiveTradingEnabled");
        var message = string.Join(" ", problems);
        if (live || !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Refusing to start: {message} Set real secrets through environment variables (Jwt__SigningKey, Credentials__EncryptionKey, Seed__AdminPassword). " +
                (live ? "Trading:LiveTradingEnabled is true." : $"Environment is {environment.EnvironmentName}."));
        }

        log.Warning("Development secrets in use: {Problems} Live trading must stay off with these values.", message);
    }
}

public static class OperatorRoles
{
    public static readonly string[] All = [RoleNames.Admin];
}
