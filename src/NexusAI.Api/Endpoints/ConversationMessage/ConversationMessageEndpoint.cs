using NexusAI.Application.ConversationMessages.Queries.GetConversationMessages;
using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Conversation;

namespace NexusAI.Api.Endpoints.Conversations;

public static class ConversationMessageEndpoint
{
    public static IEndpointRouteBuilder MapConversationMessageEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/conversations/{conversationId:guid}/messages",
            async (
                Guid conversationId,
                GetConversationMessagesHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result =
                    await handler.HandleAsync(
                        new GetConversationMessagesQuery(
                            new ConversationId(conversationId)),
                        cancellationToken);

                return Results.Ok(result);
            });

        return app;
    }
}