using Nexus.Platform.Contracts.Models;

namespace Nexus.Platform.Core.Models;

public sealed class RoutingModelGateway(IEnumerable<INamedModelGateway> gateways) : IModelGateway
{
    public Task<ModelInvocationResult> InvokeAsync(ModelInvocation invocation, CancellationToken ct = default)
    {
        var gateway = Resolve(invocation.ModelId);
        if (gateway is null)
        {
            return Task.FromResult(new ModelInvocationResult
            {
                Success = false,
                Error = $"No model gateway registered for '{invocation.ModelId}'.",
                ModelUsed = invocation.ModelId
            });
        }

        return gateway.InvokeAsync(invocation, ct);
    }

    public IAsyncEnumerable<ModelStreamChunk> StreamAsync(ModelInvocation invocation, CancellationToken ct = default)
    {
        var gateway = Resolve(invocation.ModelId)
            ?? throw new InvalidOperationException($"No model gateway registered for '{invocation.ModelId}'.");

        return gateway.StreamAsync(invocation, ct);
    }

    private INamedModelGateway? Resolve(string modelId)
    {
        var separator = modelId.IndexOf(':');
        var vendor = separator >= 0 ? modelId[..separator] : modelId;

        return gateways.FirstOrDefault(g => string.Equals(g.Vendor, vendor, StringComparison.OrdinalIgnoreCase));
    }
}
