using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NexusAI.Application.Workspaces.Commands.CreateWorkspace;
using NexusAI.Application.Workspaces.Commands.UpdateWorkspace;
using NexusAI.Application.Workspaces.Queries.GetWorkspace;
using NexusAI.Application.Workspaces.Queries.ListWorkspaces;
using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Api.Endpoints.Workspaces;

public static class WorkspaceEndpoint
{
    public static void MapWorkspaceEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/workspaces",
            async (
                [FromBody] CreateWorkspaceRequest request,
                [FromServices] CreateWorkspaceHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new CreateWorkspaceCommand(
                        request.Name,
                        request.Owner,
                        request.Description),
                    cancellationToken);

                return Results.Ok(
                    new CreateWorkspaceResponse(
                        result.WorkspaceId.Value,
                        result.Name));
            });

        app.MapGet(
            "/api/workspaces/{id:guid}",
            async (
                Guid id,
                [FromServices] GetWorkspaceHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new GetWorkspaceQuery(
                        new WorkspaceId(id)),
                    cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(
                    new GetWorkspaceResponse(
                        result.WorkspaceId.Value,
                        result.Name,
                        result.Owner,
                        result.Description,
                        (int)result.Status,
                        result.CreatedAt));
            });

        app.MapGet(
            "/api/workspaces",
            async (
                [FromServices] ListWorkspacesHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new ListWorkspacesQuery(),
                    cancellationToken);

                var workspaces = result.Workspaces
                    .Select(workspace =>
                        new GetWorkspaceResponse(
                            workspace.WorkspaceId.Value,
                            workspace.Name,
                            workspace.Owner,
                            workspace.Description,
                            (int)workspace.Status,
                            workspace.CreatedAt))
                    .ToList();

                return Results.Ok(
                    new ListWorkspacesResponse(workspaces));
            });

        app.MapPut(
            "/api/workspaces/{id:guid}",
            async (
                Guid id,
                [FromBody] UpdateWorkspaceRequest request,
                [FromServices] UpdateWorkspaceHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new UpdateWorkspaceCommand(
                        new WorkspaceId(id),
                        request.Name,
                        request.Owner,
                        request.Description),
                    cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(
                    new UpdateWorkspaceResponse(
                        result.WorkspaceId.Value,
                        result.Name));
            });
    }
}