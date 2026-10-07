using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using McpFederationGateway.Configuration;
using McpFederationGateway.Protocol;

namespace McpFederationGateway.Federation;

public interface IDownstreamMcpClient
{
    string ServerName { get; }

    Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct);

    Task<JsonElement> CallToolAsync(string name, JsonElement? arguments, CancellationToken ct);
}

public sealed class StreamableHttpMcpClient(
    DownstreamServerOptions config,
    HttpClient http,
    ILogger<StreamableHttpMcpClient> log) : IDownstreamMcpClient
{
    private string? _sessionId;
    private long _id;

    public string ServerName => config.Name;

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var result = await RpcAsync("tools/list", new { }, ct);
        return result
            .GetProperty("tools")
            .EnumerateArray()
            .Select(t => new McpTool(
                t.GetProperty("name").GetString()!,
                t.TryGetProperty("description", out var d) ? d.GetString() : null,
                t.GetProperty("inputSchema").Clone()))
            .ToArray();
    }

    public async Task<JsonElement> CallToolAsync(string name, JsonElement? arguments, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        return await RpcAsync("tools/call", new { name, arguments }, ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_sessionId is not null)
        {
            return;
        }

        await RpcAsync(
            "initialize",
            new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "mcp-federation-gateway", version = "0.1.0" }
            },
            ct);

        await NotifyAsync("notifications/initialized", new { }, ct);
        _sessionId ??= "stateless";
    }

    private HttpRequestMessage Build(object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        foreach (var header in config.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (config.BearerTokenEnvironmentVariable is { Length: > 0 } env
            && Environment.GetEnvironmentVariable(env) is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (_sessionId is not null && _sessionId != "stateless")
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        return request;
    }

    private async Task<JsonElement> RpcAsync(string method, object args, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _id);

        using var req = Build(new { jsonrpc = "2.0", id, method, @params = args });
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (res.Headers.TryGetValues("Mcp-Session-Id", out var values))
        {
            _sessionId = values.FirstOrDefault();
        }

        var body = await ReadBodyAsync(res, ct);
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            throw new InvalidOperationException($"Downstream {config.Name}: {err}");
        }

        return root.GetProperty("result").Clone();
    }

    private async Task NotifyAsync(string method, object args, CancellationToken ct)
    {
        using var req = Build(new { jsonrpc = "2.0", method, @params = args });
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var text = await res.Content.ReadAsStringAsync(ct);

        if (res.Content.Headers.ContentType?.MediaType != "text/event-stream")
        {
            return text;
        }

        var data = text
            .Split('\n')
            .Where(x => x.StartsWith("data:"))
            .Select(x => x[5..].Trim())
            .LastOrDefault(x => x.Length > 0);

        return data ?? throw new InvalidOperationException("Empty SSE response");
    }
}

public sealed class DownstreamClientFactory(
    IHttpClientFactory factory,
    IOptions<Configuration.FederationOptions> options,
    ILoggerFactory logs)
{
    public IReadOnlyList<IDownstreamMcpClient> CreateAll() =>
        options.Value.Servers
            .Where(s => s.Enabled)
            .Select(s =>
            {
                var client = factory.CreateClient(s.Name);
                client.Timeout = TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds);
                return (IDownstreamMcpClient)new StreamableHttpMcpClient(
                    s,
                    client,
                    logs.CreateLogger<StreamableHttpMcpClient>());
            })
            .ToArray();
}
