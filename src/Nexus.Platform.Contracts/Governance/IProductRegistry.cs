namespace Nexus.Platform.Contracts.Governance;

public interface IProductRegistry
{
    Task<bool> IsProductRegisteredAsync(string tenantId, string productId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListProductsAsync(string tenantId, CancellationToken ct = default);
}
