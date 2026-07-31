using NexusAI.Domain.Adr;

namespace NexusAI.Application.Adr.Commands;

public sealed class CreateAdrHandler
{
    private readonly IAdrRepository _repository;

    public CreateAdrHandler(
        IAdrRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateAdrResult> HandleAsync(
        CreateAdrCommand command,
        CancellationToken cancellationToken = default)
    {
        var adr = new NexusAI.Domain.Adr.Adr(
            AdrId.New(),
            command.KnowledgeId,
            command.Title,
            command.Decision,
            AdrStatus.Proposed,
            DateTimeOffset.UtcNow);

        await _repository.AddAsync(
            adr,
            cancellationToken);

        return new CreateAdrResult(
            adr.Id);
    }
}