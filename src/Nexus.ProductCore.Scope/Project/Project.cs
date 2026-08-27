using Nexus.ProductCore.Contracts;
using Nexus.ProductCore.Scope.Common;
using Nexus.ProductCore.Scope.Common.Identifiers;

namespace Nexus.ProductCore.Scope.Project;

/// <summary>
/// Relocated from Nexus.Products.Chat.Domain.Project (CHG-20260827-001, M-06-1.1
/// WI-06-1.1.1, task T-06-1.1.1.2). Two changes beyond a pure relocation, both called for
/// explicitly by the task: (1) now extends AggregateRoot and implements IScopeNode, matching
/// Workspace's shape, since scope is no longer a Chat-product concept; (2) gained a
/// Reference property with the same Restore-only rehydration path Workspace already proved
/// in Stage 1b (S-06-1.1.1.2.1 - PRJ- prefix, matching the live Dataverse autonumber format
/// so references a person has already seen still read the same way).
/// </summary>
public sealed class Project : AggregateRoot<ProjectId>, IScopeNode
{
    public Project(
        ProjectId id,
        WorkspaceId workspaceId,
        string name,
        DateTimeOffset createdAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        WorkspaceId = workspaceId;
        Name = name.Trim();
        CreatedAt = createdAt;
        Status = ProjectStatus.Active;
    }

    private Project(
        ProjectId id,
        WorkspaceId workspaceId,
        string name,
        ProjectStatus status,
        DateTimeOffset createdAt,
        string reference)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        Status = status;
        CreatedAt = createdAt;
        Reference = reference;
    }

    public WorkspaceId WorkspaceId { get; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public ProjectStatus Status { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    Guid IScopeNode.Id => Id.Value;

    ScopeKind IScopeNode.Kind => WellKnownScopeKinds.Project;

    Guid? IScopeNode.ParentId => WorkspaceId.Value;

    string IScopeNode.DisplayName => Name;

    // Rehydration path: mirrors Workspace.Restore exactly - only a repository restoring a
    // persisted row knows the reference the store already allocated.
    public static Project Restore(
        ProjectId id,
        WorkspaceId workspaceId,
        string name,
        ProjectStatus status,
        DateTimeOffset createdAt,
        string reference)
        => new(id, workspaceId, name, status, createdAt, reference);

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void Archive()
    {
        Status = ProjectStatus.Archived;
    }
}
