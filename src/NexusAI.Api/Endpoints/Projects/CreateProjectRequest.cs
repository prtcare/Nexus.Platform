namespace NexusAI.Api.Endpoints.Projects;

public sealed record CreateProjectRequest(
    Guid WorkspaceId,
    string Name);