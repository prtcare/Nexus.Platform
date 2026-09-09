namespace Nexus.Platform.Contracts.Models;

public sealed record ModelQuery
{
    public string? Vendor { get; init; }

    public ModelCapabilities? RequiredCapabilities { get; init; }

    public int? MinContextWindow { get; init; }

    public decimal? MaxCostPer1kIn { get; init; }

    public static ModelQuery Any { get; } = new();
}
