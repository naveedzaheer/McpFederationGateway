using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using McpFederationGateway.Configuration;

namespace McpFederationGateway.Federation;

public sealed class RegistryRefreshService(
    FederatedRegistry registry,
    IOptions<FederationOptions> options,
    ILogger<RegistryRefreshService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await registry.RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.RefreshIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await registry.RefreshAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Registry refresh failed");
            }
        }
    }
}

public sealed class RegistryHealthCheck(FederatedRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            registry.IsReady
                ? HealthCheckResult.Healthy($"{registry.ToolCount} tools registered")
                : HealthCheckResult.Unhealthy("Registry has not completed initial refresh"));
}
