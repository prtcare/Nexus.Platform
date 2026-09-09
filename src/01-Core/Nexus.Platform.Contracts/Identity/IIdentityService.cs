namespace Nexus.Platform.Contracts.Identity;

public interface IIdentityService
{
    Task<ResolvedIdentity?> ResolveAsync(string tenantId, string userId, CancellationToken ct = default);
}
