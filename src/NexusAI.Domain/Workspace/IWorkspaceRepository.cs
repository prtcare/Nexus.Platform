using NexusAI.Domain.Common;
using NexusAI.Domain.Common.Identifiers;
using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Domain.Workspace;

public interface IWorkspaceRepository
    : IRepository<Workspace, WorkspaceId>
{
}