using Nexus.ProductCore.Contracts;
using Nexus.ProductCore.Scope.Common;
using Nexus.ProductCore.Scope.Common.Identifiers;

namespace Nexus.ProductCore.Scope.Workspace;

/// <summary>
/// Relocated from Nexus.Products.Chat.Domain.Workspace (CHG-20260827-001, M-06-1.1
/// WI-06-1.1.1). The Id/Seq/Ref pattern, the private Restore-only rehydration path, and
/// every member are preserved byte-for-byte in behavior - only the namespace changed and
/// IScopeNode was added, since scope is no longer a Chat-product concept.
/// </summary>
public sealed class Workspace : AggregateRoot<WorkspaceId>, IScopeNode
{
    public Workspace(
        WorkspaceId id,
        string name,
        string owner,
        string description,
        DateTimeOffset createdAt)
        : base(id)
    {
        Rename(name);
        ChangeOwner(owner);
        ChangeDescription(description);

        CreatedAt = createdAt;
        Status = WorkspaceStatus.Active;
    }

    private Workspace(
        WorkspaceId id,
        string name,
        string owner,
        string description,
        WorkspaceStatus status,
        DateTimeOffset createdAt,
        string reference)
        : base(id)
    {
        Name = name;
        Owner = owner;
        Description = description;
        Status = status;
        CreatedAt = createdAt;
        Reference = reference;
    }

    public string Name { get; private set; } = string.Empty;

    public string Owner { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public WorkspaceStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public string Reference { get; private set; } = string.Empty;

    Guid IScopeNode.Id => Id.Value;

    ScopeKind IScopeNode.Kind => WellKnownScopeKinds.Workspace;

    Guid? IScopeNode.ParentId => null;

    string IScopeNode.DisplayName => Name;

    // Rehydration path: only a repository restoring a persisted row knows the
    // reference the store already allocated - Create() never does.
    public static Workspace Restore(
        WorkspaceId id,
        string name,
        string owner,
        string description,
        WorkspaceStatus status,
        DateTimeOffset createdAt,
        string reference)
        => new(id, name, owner, description, status, createdAt, reference);

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void ChangeOwner(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        Owner = owner.Trim();
    }

    public void ChangeDescription(string description)
    {
        Description = description?.Trim() ?? string.Empty;
    }

    public void Archive()
    {
        Status = WorkspaceStatus.Archived;
    }
}
