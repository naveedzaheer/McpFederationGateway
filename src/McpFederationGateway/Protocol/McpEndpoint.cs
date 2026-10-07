using System.Text.Json;
using McpFederationGateway.Configuration;
using McpFederationGateway.Federation;
using McpFederationGateway.Security;
using Microsoft.Extensions.Options;

namespace McpFederationGateway.Protocol;

public static class McpEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        FederatedRegistry registry,
        IToolAccessPolicy policy,
        IOptions<FederationOptions> options,
        CancellationToken ct)
    {
        JsonRpcRequest? request;

        try
        {
            request = await http.Request.ReadFromJsonAsync<JsonRpcRequest>(cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            return Results.Json(Rpc.Error(null, -32700, "Parse error", ex.Message));
        }

        if (request is null || request.JsonRpc != "2.0")
        {
            return Results.Json(Rpc.Error(request?.Id, -32600, "Invalid Request"));
        }

        if (request.Id is null)
        {
            return Results.NoContent(); // notifications/initialized and other notifications
        }

        try
        {
            return request.Method switch
            {
                "initialize" => Results.Json(Rpc.Ok(
                    request.Id,
                    new
                    {
                        protocolVersion = "2025-11-25",
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = options.Value.GatewayName, version = options.Value.GatewayVersion }
                    })),

                "ping" => Results.Json(Rpc.Ok(request.Id, new { })),

                "tools/list" => Results.Json(Rpc.Ok(
                    request.Id,
                    new
                    {
                        tools = registry.GetTools(http.User, policy).Select(x => new
                        {
                            name = x.PublicName,
                            description = x.Description,
                            inputSchema = x.InputSchema
                        })
                    })),

                "tools/call" => await CallToolAsync(request, http, registry, policy, ct),

                _ => Results.Json(Rpc.Error(request.Id, -32601, $"Method not found: {request.Method}"))
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.Json(Rpc.Error(request.Id, -32001, "Request cancelled"), statusCode: 499);
        }
        catch (Exception ex)
        {
            return Results.Json(Rpc.Error(request.Id, -32603, "Internal error", ex.Message), statusCode: 500);
        }
    }

    private static async Task<IResult> CallToolAsync(
        JsonRpcRequest request,
        HttpContext http,
        FederatedRegistry registry,
        IToolAccessPolicy policy,
        CancellationToken ct)
    {
        if (request.Params is null)
        {
            return Results.Json(Rpc.Error(request.Id, -32602, "Missing params"));
        }

        var parameters = request.Params.Value;

        if (!parameters.TryGetProperty("name", out var nameProperty)
            || string.IsNullOrWhiteSpace(nameProperty.GetString()))
        {
            return Results.Json(Rpc.Error(request.Id, -32602, "Missing tool name"));
        }

        var name = nameProperty.GetString()!;

        if (!registry.TryGet(name, out var route))
        {
            return Results.Json(Rpc.Error(request.Id, -32602, $"Unknown tool: {name}"));
        }

        if (!policy.IsAllowed(http.User, route))
        {
            return Results.Json(Rpc.Error(request.Id, -32003, $"Access denied: {name}"), statusCode: 403);
        }

        JsonElement? args = parameters.TryGetProperty("arguments", out var argumentsProperty)
            ? argumentsProperty
            : null;

        var result = await route.Client.CallToolAsync(route.OriginalName, args, ct);
        return Results.Json(Rpc.Ok(request.Id, result));
    }
}
