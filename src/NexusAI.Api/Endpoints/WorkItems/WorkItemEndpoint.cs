using Microsoft.AspNetCore.Mvc;
using NexusAI.Api.Endpoints.WorkItem;
using NexusAI.Application.WorkItem;
using NexusAI.Domain.Project;
using NexusAI.Domain.WorkItem;

namespace NexusAI.Api.Endpoints.WorkItems;

public static class WorkItemEndpoint
{
    public static IEndpointRouteBuilder MapWorkItemEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/projects/{projectId:guid}/workitems",
            async (
                Guid projectId,
                CreateWorkItemRequest request,
                [FromServices] CreateWorkItemHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new CreateWorkItemCommand(
    new ProjectId(projectId),
    request.Title,
    request.Description,
    (WorkItemType)request.Type),
                    cancellationToken);

                return Results.Ok(
                    new CreateWorkItemResponse(
                        result.WorkItemId.Value));
            });

        app.MapGet(
    "/api/workitems/{id:guid}",
    async (
        Guid id,
        [FromServices] GetWorkItemHandler handler,
        CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(
            new GetWorkItemQuery(
                new WorkItemId(id)));

        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(
            new GetWorkItemResponse(
                result.WorkItemId.Value,
                result.ProjectId.Value,
                result.Title,
                result.Description,
                (int)result.Type,
                (int)result.Status,
                result.CreatedAt));
    });

        app.MapGet(
    "/api/projects/{projectId:guid}/workitems",
    async (
        Guid projectId,
        ListWorkItemsHandler handler,
        CancellationToken cancellationToken) =>
    {
        var results = await handler.HandleAsync(
            new ListWorkItemsQuery(
                new ProjectId(projectId)),
            cancellationToken);

        return Results.Ok(
            results.Select(result =>
                new ListWorkItemsResponse(
                    result.WorkItemId.Value,
                    result.ProjectId.Value,
                    result.Title,
                    result.Description,
                    (int)result.Type,
                    (int)result.Status,
                    result.CreatedAt)));
    });


        app.MapPut(
    "/api/workitems/{id:guid}",
    async (
        Guid id,
        UpdateWorkItemRequest request,
        UpdateWorkItemHandler handler,
        CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(
            new UpdateWorkItemCommand(
                new WorkItemId(id),
                request.Title,
                request.Description,
                (WorkItemType)request.Type,
                (WorkItemStatus)request.Status),
            cancellationToken);

        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(
            new UpdateWorkItemResponse(
                result.WorkItemId.Value));
    });
        return app;
    }
}