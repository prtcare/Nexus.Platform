using NexusAI.Application.Chat.Commands.SendChat;
using NexusAI.Domain.Conversation;

namespace NexusAI.Api.Endpoints.Chat;

public static class ChatEndpoint
{
    public static IEndpointRouteBuilder MapChatEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/chat",
            async (
                SendChatRequest request,
                SendChatHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return Results.BadRequest(
                        new
                        {
                            error = "Prompt is required."
                        });
                }

                var command = new SendChatCommand(
                    new ConversationId(request.ConversationId),
                    request.Prompt);

                var result = await handler.HandleAsync(
                    command,
                    cancellationToken);

                if (!result.Success)
                {
                    return Results.BadRequest(result);
                }

                return Results.Ok(result);
            });

        return app;
    }
}