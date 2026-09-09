namespace Nexus.ProductCore.Contracts;

/// <summary>
/// Registration record for one consumer-declared scope kind (M-06-1.2, "Extensible scope
/// registration"). A layer or product declares its own scope hierarchy above or below the
/// shared Workspace -&gt; Project -&gt; Subproject trunk without Layer 06 changing.
/// </summary>
/// <param name="Kind">The kind being registered (e.g. "Feature").</param>
/// <param name="ParentKind">
/// The kind this one nests under (e.g. Developer's "Feature" nests under the shared
/// "Subproject" kind - see <see cref="WellKnownScopeKinds"/>). Null for a root kind.
/// </param>
/// <param name="Owner">
/// A human-readable label for who owns this kind (e.g. "Nexus.Developer"), used only for
/// diagnostics - Layer 06 must never branch on this value.
/// </param>
public sealed record ScopeKindRegistration(
    ScopeKind Kind,
    ScopeKind? ParentKind,
    string Owner);

/// <summary>
/// Registry consumers use to declare their own scope kinds. The registry itself must never
/// contain a branch keyed on a specific consumer's identity - it is a plain lookup table,
/// nothing more. An architecture test enforces this (see M-06-1.2 acceptance).
/// </summary>
public interface IScopeKindRegistry
{
    /// <summary>
    /// Registers a scope kind. Throws <see cref="InvalidOperationException"/> if the kind is
    /// already registered (registration is one-time, at startup, not a runtime toggle).
    /// </summary>
    void Register(ScopeKindRegistration registration);

    /// <summary>True if this kind has been registered (including the well-known trunk kinds).</summary>
    bool IsRegistered(ScopeKind kind);

    /// <summary>Every registration recorded so far, in registration order.</summary>
    IReadOnlyList<ScopeKindRegistration> All { get; }
}

/// <summary>
/// The three trunk kinds Layer 06 itself owns. Every consumer-registered kind's chain of
/// <see cref="ScopeKindRegistration.ParentKind"/> must terminate at one of these (or at
/// another consumer-registered kind that itself terminates here).
/// </summary>
public static class WellKnownScopeKinds
{
    public static readonly ScopeKind Workspace = new("Workspace");
    public static readonly ScopeKind Project = new("Project");
    public static readonly ScopeKind Subproject = new("Subproject");
}
