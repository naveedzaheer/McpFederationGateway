using McpFederationGateway;
using McpFederationGateway.Configuration;
using McpFederationGateway.Federation;
using McpFederationGateway.Protocol;
using McpFederationGateway.Security;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddOptions<FederationOptions>()
    .Bind(builder.Configuration.GetSection("Federation"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DownstreamClientFactory>();
builder.Services.AddSingleton<FederatedRegistry>();
builder.Services.AddSingleton<IToolAccessPolicy, ConfigurationToolAccessPolicy>();
builder.Services.AddHostedService<RegistryRefreshService>();
builder.Services.AddHealthChecks().AddCheck<RegistryHealthCheck>("federation-registry");

var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.MapHealthChecks("/healthz");
app.MapGet(
    "/readyz",
    (FederatedRegistry registry) =>
        registry.IsReady
            ? Results.Ok(new { status = "ready", tools = registry.ToolCount })
            : Results.StatusCode(503));
app.MapGet("/admin/registry", (FederatedRegistry registry) => Results.Ok(registry.Snapshot()));
app.MapPost(
    "/admin/refresh",
    async (FederatedRegistry registry, CancellationToken ct) =>
    {
        await registry.RefreshAsync(ct);
        return Results.Ok(registry.Snapshot());
    });
app.MapMethods("/mcp", new[] { "POST" }, McpEndpoint.HandleAsync);
app.Run();
