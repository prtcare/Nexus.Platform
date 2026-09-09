namespace Nexus.ProductCore.Contracts;

/// <summary>
/// A generic scope-tree node, as understood by any consumer of the shared Workspace -&gt;
/// Project -&gt; Subproject trunk owned by Layer 06 Product Core. Concrete aggregates
/// (Workspace, Project, Subproject, and every consumer-registered kind below Subproject -
/// Developer's Feature/Task/Subtask, a machine domain's own hierarchy, etc.) implement this
/// so callers can resolve context (e.g. "what conversation context does this node carry")
/// without referencing the consumer's own assembly. No consumer type may appear in this
/// assembly - see M-06-1.1 acceptance criteria in nexus-roadmap.yaml.
/// </summary>
public interface IScopeNode
{
    Guid Id { get; }

    ScopeKind Kind { get; }

    Guid? ParentId { get; }

    string DisplayName { get; }
}
