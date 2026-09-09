namespace Nexus.Platform.Contracts.Secrets;

public interface ISecretResolver
{
    Task<string?> ResolveAsync(string key, CancellationToken ct = default);
}
