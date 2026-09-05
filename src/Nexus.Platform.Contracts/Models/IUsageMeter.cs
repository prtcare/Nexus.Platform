namespace Nexus.Platform.Contracts.Models;

public interface IUsageMeter
{
    Task RecordAsync(UsageRecord record, CancellationToken ct = default);
}
