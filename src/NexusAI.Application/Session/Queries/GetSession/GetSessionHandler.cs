using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Queries.GetSession;

public sealed class GetSessionHandler
{
    private readonly ISessionRepository _repository;

    public GetSessionHandler(
        ISessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetSessionResult?> HandleAsync(
        GetSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetAsync(
            query.SessionId,
            cancellationToken);

        if (session is null)
        {
            return null;
        }

        return new GetSessionResult(
            session.Id,
            session.ConversationId,
            session.Status,
            session.StartedAt,
            session.EndedAt);
    }
}