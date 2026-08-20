namespace Nexus.Platform.Contracts.Models;

public interface IModelGateway
{
    Task<ModelInvocationResult> InvokeAsync(ModelInvocation invocation, CancellationToken ct = default);

    IAsyncEnumerable<ModelStreamChunk> StreamAsync(ModelInvocation invocation, CancellationToken ct = default);
}
