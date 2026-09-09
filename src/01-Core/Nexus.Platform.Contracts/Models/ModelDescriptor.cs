namespace Nexus.Platform.Contracts.Models;

public sealed record ModelDescriptor(
    string ModelId,
    string Vendor,
    ModelCapabilities Capabilities,
    int ContextWindow,
    decimal CostPer1kIn,
    decimal CostPer1kOut,
    LatencyClass Latency);
