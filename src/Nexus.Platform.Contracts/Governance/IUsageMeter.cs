namespace Nexus.Platform.Contracts.Governance;

public interface IUsageMeter
{
    Task RecordAsync(UsageRecord record, CancellationToken ct = default);
}
