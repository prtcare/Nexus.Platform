namespace NexusAI.Api.Endpoints;

public static class PlatformHealthEndpoint
{
    public static IEndpointRouteBuilder MapPlatformHealthEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () =>
        {
            return Results.Ok(new
            {
                status = "Healthy",
                service = "NexusAI.Api",
                version = typeof(Program)
                    .Assembly
                    .GetName()
                    .Version?
                    .ToString()
            });
        });

        return endpoints;
    }
}