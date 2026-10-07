using System.ComponentModel.DataAnnotations;

namespace McpFederationGateway.Configuration;

public sealed class FederationOptions
{
    public string GatewayName { get; set; } = "mcp-federation-gateway";

    public string GatewayVersion { get; set; } = "0.1.0";

    [Range(5, 3600)]
    public int RefreshIntervalSeconds { get; set; } = 60;

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 60;

    public List<DownstreamServerOptions> Servers { get; set; } = [];

    public List<string> DeniedTools { get; set; } = [];
}

public sealed class DownstreamServerOptions
{
    [Required]
    public string Name { get; set; } = "";

    [Required, Url]
    public string Endpoint { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Namespace { get; set; } = "";

    public Dictionary<string, string> Headers { get; set; } = [];

    public string? BearerTokenEnvironmentVariable { get; set; }
}
