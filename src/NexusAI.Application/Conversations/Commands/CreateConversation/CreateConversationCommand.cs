using NexusAI.Domain.Project;

namespace NexusAI.Application.Conversations.Commands.CreateConversation;

public sealed record CreateConversationCommand(
    ProjectId ProjectId,
    string Title);