using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using McpFederationGateway.Configuration;
using McpFederationGateway.Protocol;
using McpFederationGateway.Security;
using Microsoft.Extensions.Options;

namespace McpFederationGateway.Federation;

public sealed record ToolRoute(
    string PublicName,
    string OriginalName,
    string ServerName,
    string? Description,
    JsonElement InputSchema,
    IDownstreamMcpClient Client);

public sealed class FederatedRegistry(
    DownstreamClientFactory factory,
    IOptions<FederationOptions> options,
    ILogger<FederatedRegistry> log)
{
    private volatile IReadOnlyDictionary<string, ToolRoute> _routes = new Dictionary<string, ToolRoute>();

    public bool IsReady { get; private set; }

    public int ToolCount => _routes.Count;

    public bool TryGet(string name, out ToolRoute route) => _routes.TryGetValue(name, out route!);

    public IEnumerable<ToolRoute> GetTools(ClaimsPrincipal user, IToolAccessPolicy policy) =>
        _routes.Values.Where(x => policy.IsAllowed(user, x)).OrderBy(x => x.PublicName);

    public object Snapshot() => new
    {
        ready = IsReady,
        toolCount = ToolCount,
        tools = _routes.Values.Select(x => new
        {
            x.PublicName,
            x.OriginalName,
            x.ServerName,
            x.Description
        })
    };

    public async Task RefreshAsync(CancellationToken ct)
    {
        var next = new Dictionary<string, ToolRoute>(StringComparer.OrdinalIgnoreCase);

        foreach (var client in factory.CreateAll())
        {
            try
            {
                var cfg = options.Value.Servers.Single(x => x.Name == client.ServerName);
                var ns = string.IsNullOrWhiteSpace(cfg.Namespace) ? cfg.Name : cfg.Namespace;

                foreach (var tool in await client.ListToolsAsync(ct))
                {
                    var publicName = $"{ns}.{tool.Name}";
                    if (!next.TryAdd(publicName, new(publicName, tool.Name, cfg.Name, tool.Description, tool.InputSchema, client)))
                    {
                        throw new InvalidOperationException($"Duplicate federated tool: {publicName}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to refresh downstream server {Server}", client.ServerName);
            }
        }

        _routes = next;
        IsReady = true;
        log.LogInformation("Federated registry contains {Count} tools", next.Count);
    }
}
