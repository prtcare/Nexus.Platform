using Nexus.Platform.Contracts.Core;

namespace Nexus.Platform.Core.ProductCore;

// Allows every invocation. Replace with a real policy once entitlements exist.
public sealed class PermissiveQuotaPolicy : IQuotaPolicy
{
    public Task<QuotaVerdict> CheckAsync(InvocationIdentity identity, string modelId, CancellationToken ct = default)
        => Task.FromResult(QuotaVerdict.Allow());
}
