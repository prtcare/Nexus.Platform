namespace Nexus.Platform.Contracts.Governance;

public interface IAuditLog
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct = default);
}
