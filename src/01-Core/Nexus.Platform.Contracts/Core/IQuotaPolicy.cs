namespace Nexus.Platform.Contracts.Core;

public interface IQuotaPolicy
{
    Task<QuotaVerdict> CheckAsync(InvocationIdentity identity, string modelId, CancellationToken ct = default);
}
