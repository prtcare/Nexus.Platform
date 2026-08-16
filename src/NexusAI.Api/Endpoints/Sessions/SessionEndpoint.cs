using NexusAI.Application.Session.Commands;
using NexusAI.Application.Session.Commands.UpdateSession;
using NexusAI.Application.Session.Queries.GetSession;
using NexusAI.Application.Session.Queries.ListSessions;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Session;

namespace NexusAI.Api.Endpoints.Sessions;

public static class SessionEndpoint
{
    public static IEndpointRouteBuilder MapSessionEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/sessions",
            async (
                CreateSessionRequest request,
                CreateSessionHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result =
                    await handler.HandleAsync(
                        new CreateSessionCommand(
                            new ConversationId(
                                request.ConversationId)),
                        cancellationToken);

                return Results.Ok(
                    new CreateSessionResponse(
                        result.SessionId.Value,
                        (int)result.Status,
                        result.StartedAt));
            });

        app.MapGet(
            "/api/sessions/{id:guid}",
            async (
                Guid id,
                GetSessionHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result =
                    await handler.HandleAsync(
                        new GetSessionQuery(
                            new SessionId(id)),
                        cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(
                    new GetSessionResponse(
                        result.SessionId.Value,
                        result.ConversationId.Value,
                        (int)result.Status,
                        result.StartedAt,
                        result.EndedAt));
            });

        app.MapGet(
            "/api/conversations/{conversationId:guid}/sessions",
            async (
                Guid conversationId,
                ListSessionsHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result =
                    await handler.HandleAsync(
                        new ListSessionsQuery(
                            new ConversationId(
                                conversationId)),
                        cancellationToken);

                return Results.Ok(
                    result.Select(session =>
                        new ListSessionResponse(
                            session.SessionId.Value,
                            (int)session.Status,
                            session.StartedAt,
                            session.EndedAt)));
            });

        app.MapPut(
            "/api/sessions/{id:guid}",
            async (
                Guid id,
                UpdateSessionRequest request,
                UpdateSessionHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (!Enum.IsDefined(
                        typeof(SessionStatus),
                        request.Status))
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                $"Unsupported SessionStatus: {request.Status}."
                        });
                }

                var status =
                    (SessionStatus)request.Status;

                var result =
                    await handler.HandleAsync(
                        new UpdateSessionCommand(
                            new SessionId(id),
                            status),
                        cancellationToken);

                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(
                    new
                    {
                        sessionId =
                            result.SessionId.Value,

                        status =
                            (int)result.Status,

                        result.StartedAt,
                        result.EndedAt
                    });
            });

        return app;
    }
}