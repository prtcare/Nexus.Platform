using Nexus.ProductCore.Contracts;
using Nexus.ProductCore.Scope.Common;
using Nexus.ProductCore.Scope.Common.Identifiers;

namespace Nexus.ProductCore.Scope.Subproject;

/// <summary>
/// New aggregate (CHG-20260827-001, M-06-1.1 WI-06-1.1.1, task T-06-1.1.1.2). The trunk's
/// third level: Workspace -&gt; Project -&gt; Subproject. Consumers (Developer's Feature, a
/// machine domain's own root, etc.) extend below this. Reference prefix is SPR- (not
/// documented in nexus-roadmap.yaml since Subproject is new here - deliberately distinct
/// from Developer's existing SUB- Subtask prefix to avoid a human ever seeing two different
/// concepts abbreviate to the same letters).
/// </summary>
public sealed class Subproject : AggregateRoot<SubprojectId>, IScopeNode
{
    public Subproject(
        SubprojectId id,
        ProjectId projectId,
        string name,
        string description,
        DateTimeOffset createdAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ProjectId = projectId;
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        CreatedAt = createdAt;
        Status = SubprojectStatus.Active;
    }

    private Subproject(
        SubprojectId id,
        ProjectId projectId,
        string name,
        string description,
        SubprojectStatus status,
        DateTimeOffset createdAt,
        string reference)
        : base(id)
    {
        ProjectId = projectId;
        Name = name;
        Description = description;
        Status = status;
        CreatedAt = createdAt;
        Reference = reference;
    }

    public ProjectId ProjectId { get; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public SubprojectStatus Status { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    Guid IScopeNode.Id => Id.Value;

    ScopeKind IScopeNode.Kind => WellKnownScopeKinds.Subproject;

    Guid? IScopeNode.ParentId => ProjectId.Value;

    string IScopeNode.DisplayName => Name;

    public static Subproject Restore(
        SubprojectId id,
        ProjectId projectId,
        string name,
        string description,
        SubprojectStatus status,
        DateTimeOffset createdAt,
        string reference)
        => new(id, projectId, name, description, status, createdAt, reference);

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void ChangeDescription(string description)
    {
        Description = description?.Trim() ?? string.Empty;
    }

    public void Archive()
    {
        Status = SubprojectStatus.Archived;
    }
}
