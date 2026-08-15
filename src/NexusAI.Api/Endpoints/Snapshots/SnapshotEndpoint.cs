using NexusAI.Application.Snapshot.Commands;
using NexusAI.Application.Snapshot.Commands.UpdateSnapshot;
using NexusAI.Application.Snapshot.Queries.GetSnapshot;
using NexusAI.Application.Snapshot.Queries.ListSnapshots;
using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Snapshot;

namespace NexusAI.Api.Endpoints.Snapshots;

public static class SnapshotEndpoint
{
    public static IEndpointRouteBuilder MapSnapshotEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
    "/api/snapshots",
    async (
        CreateSnapshotRequest request,
        CreateSnapshotHandler handler,
        CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(
            new CreateSnapshotCommand(
                new BranchId(request.BranchId),
                new ConversationId(request.ConversationId),
                request.Name,
                request.State),
            cancellationToken);

        return Results.Ok(
            new CreateSnapshotResponse(
                result.SnapshotId.Value,
                result.Name));
    });

        app.MapGet(
            "/api/snapshots/{id:guid}",
            async (
                Guid id,
                GetSnapshotHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new GetSnapshotQuery(
                        new SnapshotId(id)),
                    cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(
                    new GetSnapshotResponse(
                        result.SnapshotId.Value,
                        result.BranchId.Value,
                        result.ConversationId.Value,
                        result.Name,
                        result.State,
                        result.CreatedAt));
            });

        app.MapGet(
            "/api/branches/{branchId:guid}/snapshots",
            async (
                Guid branchId,
                ListSnapshotsHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new ListSnapshotsQuery(
                        new BranchId(branchId)),
                    cancellationToken);

                return Results.Ok(
                    result.Select(snapshot =>
                        new ListSnapshotResponse(
                            snapshot.SnapshotId.Value,
                            snapshot.Name,
                            snapshot.State,
                            snapshot.CreatedAt)));
            });

        app.MapPut(
            "/api/snapshots/{id:guid}",
            async (
                Guid id,
                UpdateSnapshotRequest request,
                UpdateSnapshotHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new UpdateSnapshotCommand(
                        new SnapshotId(id),
                        request.Name,
                        request.State),
                    cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(result);
            });

        return app;
    }
}