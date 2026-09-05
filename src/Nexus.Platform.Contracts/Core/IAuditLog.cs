namespace Nexus.Platform.Contracts.Core;

public interface IAuditLog
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct = default);
}
