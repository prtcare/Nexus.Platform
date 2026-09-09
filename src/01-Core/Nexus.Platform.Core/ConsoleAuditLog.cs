using Nexus.Platform.Contracts.Core;

namespace Nexus.Platform.Core;

public sealed class ConsoleAuditLog : IAuditLog
{
    public Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        Console.WriteLine(
            $"[AUDIT] {entry.OccurredAt:O} tenant={entry.Identity.TenantId} product={entry.Identity.ProductId} " +
            $"turn={entry.Identity.TurnId} user={entry.Identity.UserId} action={entry.Action} " +
            $"success={entry.Success} detail={entry.Detail}");

        return Task.CompletedTask;
    }
}
