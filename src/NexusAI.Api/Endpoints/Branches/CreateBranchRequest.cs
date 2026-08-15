namespace NexusAI.Api.Endpoints.Branches;

public sealed record CreateBranchRequest(
    Guid ConversationId,
    string Name,
    string Description);