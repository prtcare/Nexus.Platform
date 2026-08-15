using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Commands.UpdateConversation;

public sealed class UpdateConversationHandler
{
    private readonly IConversationRepository _repository;

    public UpdateConversationHandler(
        IConversationRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateConversationResult?> HandleAsync(
        UpdateConversationCommand command,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _repository.GetAsync(
            command.ConversationId,
            cancellationToken);

        if (conversation is null)
            return null;

        conversation.Rename(command.Title);

        conversation.ChangeDescription(
            command.Description);

        conversation.ChangeType(
            command.Type);

        conversation.ChangeVisibility(
            command.Visibility);

        await _repository.UpdateAsync(
            conversation,
            cancellationToken);

        return new UpdateConversationResult(
            conversation.Id,
            conversation.Title);
    }
}