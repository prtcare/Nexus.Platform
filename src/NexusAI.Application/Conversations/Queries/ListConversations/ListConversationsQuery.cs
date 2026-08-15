using NexusAI.Domain.Project;

namespace NexusAI.Application.Conversations.Queries.ListConversations;

public sealed record ListConversationsQuery(
    ProjectId ProjectId);