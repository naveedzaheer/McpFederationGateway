using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpFederationGateway.Protocol;

public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

public sealed record McpTool(string Name, string? Description, JsonElement InputSchema);

public static class Rpc
{
    public static object Ok(JsonElement? id, object result) => new { jsonrpc = "2.0", id, result };

    public static object Error(JsonElement? id, int code, string message, object? data = null) =>
        new { jsonrpc = "2.0", id, error = new { code, message, data } };
}
