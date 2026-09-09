namespace Nexus.Platform.Contracts.Identity;

public interface ITenantResolver
{
    Task<string?> ResolveTenantIdAsync(string key, CancellationToken ct = default);
}
