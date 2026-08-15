using NexusAI.Domain.ConversationMessage;

namespace NexusAI.Application.ConversationMessages.Queries.GetConversationMessages;

public sealed record ConversationMessageResult(
    ConversationMessageId MessageId,
    ConversationMessageRole Role,
    string Content,
    DateTimeOffset CreatedOn);