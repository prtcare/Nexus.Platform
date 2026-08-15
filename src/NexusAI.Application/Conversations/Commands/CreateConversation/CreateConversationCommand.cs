using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Project;

namespace NexusAI.Application.Conversations.Commands.CreateConversation;

public sealed record CreateConversationCommand(
    ProjectId ProjectId,
    WorkspaceId WorkspaceId,
    string Title,
    string Description,
    ConversationType Type,
    ConversationVisibility Visibility);