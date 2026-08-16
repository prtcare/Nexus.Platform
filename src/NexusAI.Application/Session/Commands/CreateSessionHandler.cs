using NexusAI.Core.Abstractions;
using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Commands;

public sealed class CreateSessionHandler
{
    private readonly ISessionRepository _repository;
    private readonly IClock _clock;

    public CreateSessionHandler(
        ISessionRepository repository,
        IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<CreateSessionResult> HandleAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = new Domain.Session.Session(
            SessionId.New(),
            command.ConversationId,
            SessionStatus.Running,
            _clock.UtcNow);

        await _repository.AddAsync(
            session,
            cancellationToken);

        return new CreateSessionResult(
            session.Id,
            session.Status,
            session.StartedAt);
    }
}