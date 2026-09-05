using Nexus.Platform.Contracts.Models;

namespace Nexus.Platform.Contracts.Governance;

public interface IQuotaPolicy
{
    Task<QuotaVerdict> CheckAsync(InvocationIdentity identity, string modelId, CancellationToken ct = default);
}
