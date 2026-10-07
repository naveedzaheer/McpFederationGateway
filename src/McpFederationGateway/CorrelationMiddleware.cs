namespace McpFederationGateway;

public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers["x-correlation-id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("n");

        context.Response.Headers["x-correlation-id"] = id;

        using (context.RequestServices.GetRequiredService<ILogger<CorrelationMiddleware>>().BeginScope(
                   new Dictionary<string, object> { { "CorrelationId", id } }))
        {
            await next(context);
        }
    }
}
