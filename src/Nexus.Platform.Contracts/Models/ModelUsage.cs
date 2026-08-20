namespace Nexus.Platform.Contracts.Models;

public sealed record ModelUsage(int TokensIn, int TokensOut, decimal EstimatedCost)
{
    public static ModelUsage Zero { get; } = new(0, 0, 0m);
}
