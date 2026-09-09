using Nexus.ProductCore.Scope.Common;
using Nexus.ProductCore.Scope.Common.Identifiers;

namespace Nexus.ProductCore.Scope.Subproject;

public interface ISubprojectRepository
    : IRepository<Subproject, SubprojectId>
{
    Task<Subproject?> GetByIdAsync(
        SubprojectId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subproject>> ListByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);
}
