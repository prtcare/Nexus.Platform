using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Queries.ListSessions;

public sealed class ListSessionsHandler
{
    private readonly ISessionRepository _repository;

    public ListSessionsHandler(
        ISessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ListSessionResult>> HandleAsync(
        ListSessionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var sessions =
            await _repository.ListByConversationAsync(
                query.ConversationId,
                cancellationToken);

        return sessions
            .OrderByDescending(session => session.StartedAt)
            .Select(session =>
                new ListSessionResult(
                    session.Id,
                    session.Status,
                    session.StartedAt,
                    session.EndedAt))
            .ToList();
    }
}