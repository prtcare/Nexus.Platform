using Nexus.Platform.Contracts.Governance;
using Nexus.Platform.Contracts.Models;

namespace Nexus.Platform.Core.Governance;

// Allows every invocation. Replace with a real policy once entitlements exist.
public sealed class PermissiveQuotaPolicy : IQuotaPolicy
{
    public Task<QuotaVerdict> CheckAsync(InvocationIdentity identity, string modelId, CancellationToken ct = default)
        => Task.FromResult(QuotaVerdict.Allow());
}
