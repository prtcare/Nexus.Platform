namespace Nexus.Platform.Contracts.Tools;

public interface IToolCatalog
{
    Task<IReadOnlyList<ToolDescriptor>> ListAsync(CancellationToken ct = default);
}
