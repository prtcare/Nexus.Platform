namespace Nexus.ProductCore.Contracts;

/// <summary>
/// An extensible tag identifying what kind of scope node an <see cref="IScopeNode"/> is
/// (e.g. "Workspace", "Project", "Subproject", or a consumer-registered kind such as
/// Developer's "Feature"/"Task"/"Subtask"). Deliberately a wrapped string, not an enum -
/// Layer 06 must never enumerate consumer kinds (see <see cref="IScopeKindRegistry"/>).
/// </summary>
public readonly record struct ScopeKind(string Value)
{
    public override string ToString()
        => Value;
}
