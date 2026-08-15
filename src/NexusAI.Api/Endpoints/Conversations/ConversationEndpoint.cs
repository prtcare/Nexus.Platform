using NexusAI.Application.Conversations.Commands.CreateConversation;
using NexusAI.Application.Conversations.Queries.GetConversation;
using NexusAI.Application.Conversations.Queries.ListConversations;
using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Project;

namespace NexusAI.Api.Endpoints.Conversations;

public static class ConversationEndpoint
{
    public static IEndpointRouteBuilder MapConversationEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/conversations",
            async (
                CreateConversationRequest request,
                CreateConversationHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result =
                    await handler.HandleAsync(
                        new CreateConversationCommand(
                            new ProjectId(request.ProjectId),
                            new WorkspaceId(request.WorkspaceId),
                            request.Title,
                            request.Description,
                            request.Type,
                            request.Visibility),
                        cancellationToken);

                return Results.Ok(
                    new CreateConversationResponse(
                        result.ConversationId.Value,
                        result.Title));
            });

        app.MapGet(
            "/api/conversations/{id:guid}",
            async (
                Guid id,
                GetConversationHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result =
                    await handler.HandleAsync(
                        new GetConversationQuery(
                            new ConversationId(id)),
                        cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(
                    new GetConversationResponse(
                        result.ConversationId.Value,
                        result.Title,
                        result.CreatedAt));
            });

        app.MapGet(
            "/api/projects/{projectId:guid}/conversations",
            async (
                Guid projectId,
                ListConversationsHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result =
                    await handler.HandleAsync(
                        new ListConversationsQuery(
                            new ProjectId(projectId)),
                        cancellationToken);

                return Results.Ok(result);
            });

        return app;
    }
}