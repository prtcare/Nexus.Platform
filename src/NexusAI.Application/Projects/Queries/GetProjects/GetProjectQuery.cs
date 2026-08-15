using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;

namespace NexusAI.Application.Projects.Queries.GetProject;

public sealed record GetProjectQuery(
    ProjectId ProjectId,
    CancellationToken CancellationToken = default);