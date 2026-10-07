using System.Security.Claims;
using McpFederationGateway.Configuration;
using McpFederationGateway.Federation;
using Microsoft.Extensions.Options;

namespace McpFederationGateway.Security;

public interface IToolAccessPolicy
{
    bool IsAllowed(ClaimsPrincipal user, ToolRoute route);
}

public sealed class ConfigurationToolAccessPolicy(IOptions<FederationOptions> options) : IToolAccessPolicy
{
    public bool IsAllowed(ClaimsPrincipal user, ToolRoute route) =>
        !options.Value.DeniedTools.Any(pattern => Matches(pattern, route.PublicName));

    private static bool Matches(string pattern, string value) =>
        pattern.EndsWith('*')
            ? value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
}
