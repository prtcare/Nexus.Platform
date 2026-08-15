using Microsoft.AspNetCore.Mvc;
using NexusAI.Application.Projects.Commands.CreateProject;
using NexusAI.Application.Projects.Queries.GetProject;
using NexusAI.Application.Projects.Queries.ListProjects;
using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;
namespace NexusAI.Api.Endpoints.Projects;
using NexusAI.Application.Projects.Commands.UpdateProject;

public static class ProjectEndpoint
{
    public static IEndpointRouteBuilder MapProjectEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/projects",
            async (
    CreateProjectRequest request,
    [FromServices] CreateProjectHandler handler,
    CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
    new CreateProjectCommand(
        new WorkspaceId(request.WorkspaceId),
        request.Name,
        cancellationToken));

                return Results.Ok(
                    new CreateProjectResponse(
                        result.ProjectId.Value,
                        result.Name));
            });


        app.MapGet(
    "/api/projects/{id:guid}",
    async (
        Guid id,
        GetProjectHandler handler,
        CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(
            new GetProjectQuery(
                new ProjectId(id),
                cancellationToken));

        if (result is null)
            return Results.NotFound();

        return Results.Ok(
            new GetProjectResponse(
                result.ProjectId.Value,
                result.WorkspaceId.Value,
                result.Name,
                result.CreatedAt));
    });

        app.MapGet(
    "/api/workspaces/{workspaceId:guid}/projects",
    async (
        Guid workspaceId,
        ListProjectsHandler handler,
        CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(
            new ListProjectsQuery(
                new WorkspaceId(workspaceId),
                cancellationToken));

        return Results.Ok(
            result.Select(x =>
                new ListProjectsResponse(
                    x.ProjectId,
                    x.Name,
                    x.CreatedAt)));
    });

        app.MapPut(
     "/api/projects/{id:guid}",
     async (
         Guid id,
         UpdateProjectRequest request,
         UpdateProjectHandler handler,
         CancellationToken cancellationToken) =>
     {
         var result = await handler.HandleAsync(
             new UpdateProjectCommand(
                 new ProjectId(id),
                 request.Name,
                 cancellationToken));

         if (result is null)
         {
             return Results.NotFound();
         }

         return Results.Ok(
             new UpdateProjectResponse(
                 result.ProjectId.Value,
                 result.Name));
     });


        return app;
    }
}