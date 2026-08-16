using NexusAI.Core.Abstractions;
using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Commands.UpdateSession;

public sealed class UpdateSessionHandler
{
    private readonly ISessionRepository _repository;
    private readonly IClock _clock;

    public UpdateSessionHandler(
        ISessionRepository repository,
        IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<UpdateSessionResult?> HandleAsync(
        UpdateSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetAsync(
            command.SessionId,
            cancellationToken);

        if (session is null)
        {
            return null;
        }

        if (command.Status == SessionStatus.Ended)
        {
            session.End(_clock.UtcNow);
        }
        else if (command.Status == SessionStatus.Running)
        {
            if (session.Status == SessionStatus.Ended)
            {
                throw new InvalidOperationException(
                    "Cannot transition an ended session back to Running.");
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.Status),
                command.Status,
                "Unsupported SessionStatus value.");
        }

        await _repository.UpdateAsync(
            session,
            cancellationToken);

        return new UpdateSessionResult(
            session.Id,
            session.Status,
            session.StartedAt,
            session.EndedAt);
    }
}