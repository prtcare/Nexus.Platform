using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Commands;

public sealed class CreateSessionHandler
{
    private readonly ISessionRepository _repository;

    public CreateSessionHandler(
        ISessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateSessionResult> HandleAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = new NexusAI.Domain.Session.Session(
            new SessionId(Guid.NewGuid()),
            command.ConversationId,
            SessionStatus.Active,
            DateTimeOffset.UtcNow);

        await _repository.AddAsync(
            session,
            cancellationToken);

        return new CreateSessionResult(session.Id);
    }
}