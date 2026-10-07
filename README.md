# MCP Federation Gateway

A policy-ready ASP.NET Core gateway that exposes one Streamable HTTP MCP endpoint and federates `tools/list` and `tools/call` across multiple downstream MCP servers.

## Included
- Dynamic downstream tool discovery and periodic refresh
- Stable namespacing: `<namespace>.<downstream-tool>`
- JSON-RPC initialize, ping, tools/list, and tools/call
- Downstream Streamable HTTP JSON and SSE response support
- Downstream session ID propagation
- Static headers and bearer-token environment variables
- Deny-list policy extension point
- Liveness/readiness and registry diagnostics
- Correlation IDs, Dockerfile, and safe localhost host filtering

## Run
```bash
dotnet restore
dotnet run --project src/McpFederationGateway
```
The upstream MCP endpoint is `POST /mcp`. Diagnostics are `/healthz`, `/readyz`, and `/admin/registry`.

## Configure
Edit `Federation:Servers` in `appsettings.json`. Each enabled server needs a unique `Name` or `Namespace`, and its Streamable HTTP MCP endpoint. Put secrets in environment variables, never in configuration:
```bash
export MCP_SERVER_A_TOKEN='replace-me'
```

## Test discovery
```bash
curl -s http://localhost:5000/mcp -H 'content-type: application/json' -H 'accept: application/json, text/event-stream' -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}'
curl -s http://localhost:5000/mcp -H 'content-type: application/json' -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

## Production gaps intentionally left as extension points
This skeleton handles tool federation. Before production, add upstream OAuth/Entra validation, per-user or on-behalf-of downstream tokens, SSRF-safe endpoint allowlisting, distributed registry/session state for scale-out, retry/circuit-breaker policy, audit redaction, and prompts/resources federation if required.

## SDK integration option
The upstream endpoint is implemented at the MCP JSON-RPC boundary because the dynamically aggregated tool catalog must be produced at runtime. `IDownstreamMcpClient` isolates transport. You can replace `StreamableHttpMcpClient` with an adapter over the official C# SDK without changing the registry or routing policy.
