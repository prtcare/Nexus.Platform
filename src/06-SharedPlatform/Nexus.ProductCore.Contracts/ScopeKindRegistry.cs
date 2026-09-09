using System.Collections.Concurrent;

namespace Nexus.ProductCore.Contracts;

/// <summary>
/// M-06-1.2 "Extensible scope registration". A plain lookup table - deliberately contains no
/// branch keyed on a specific consumer's identity, so a machine-domain consumer (or any
/// future product) can register an entirely different hierarchy without a code change here.
/// The three well-known trunk kinds are pre-registered so a consumer's first registration
/// can immediately declare a parent of "Subproject" without a chicken-and-egg ordering
/// requirement.
///
/// Lives in Nexus.ProductCore.Contracts (moved here from Nexus.ProductCore.Scope in
/// CHG-20260827-002) because it has no dependency on the Workspace/Project/Subproject domain
/// - only on the types already in this file's own assembly (ScopeKind,
/// ScopeKindRegistration, WellKnownScopeKinds). That makes it something every consumer,
/// including Nexus.Developer, can reference directly via the Contracts-only pattern already
/// established for the DEVELOPER -&gt; EXPERIENCE relationship (CHANGE_REPORT_v2.1 #10) -
/// without taking on Nexus.ProductCore.Scope's concrete domain assembly, which AGENTS.md's
/// boundary rule (no product/foreign domain assembly references) would otherwise forbid.
///
/// Deliberately per-process, not shared across hosts (CHG-20260827-002): Nexus.Developer.Api,
/// the Chat Api, and Product Core each run as separate processes today, so an instance
/// registered in one host is invisible to another. Cross-process scope resolution (a real
/// persisted ScopeKindRegistration table + an HTTP endpoint) is an explicit, known Phase 1
/// gap - not solved by this class - deferred until a consumer actually needs it.
/// </summary>
public sealed class ScopeKindRegistry : IScopeKindRegistry
{
    private readonly ConcurrentDictionary<ScopeKind, ScopeKindRegistration> _registrations = new();

    public ScopeKindRegistry()
    {
        RegisterTrunkKind(WellKnownScopeKinds.Workspace, parentKind: null);
        RegisterTrunkKind(WellKnownScopeKinds.Project, WellKnownScopeKinds.Workspace);
        RegisterTrunkKind(WellKnownScopeKinds.Subproject, WellKnownScopeKinds.Project);
    }

    public void Register(ScopeKindRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (registration.ParentKind is { } parentKind && !_registrations.ContainsKey(parentKind))
        {
            throw new InvalidOperationException(
                $"Cannot register scope kind '{registration.Kind}': its declared parent kind " +
                $"'{parentKind}' is not registered. Register parent kinds before their children.");
        }

        if (!_registrations.TryAdd(registration.Kind, registration))
        {
            throw new InvalidOperationException(
                $"Scope kind '{registration.Kind}' is already registered by " +
                $"'{_registrations[registration.Kind].Owner}'.");
        }
    }

    public bool IsRegistered(ScopeKind kind)
        => _registrations.ContainsKey(kind);

    public IReadOnlyList<ScopeKindRegistration> All
        => _registrations.Values.ToList();

    private void RegisterTrunkKind(ScopeKind kind, ScopeKind? parentKind)
        => _registrations[kind] = new ScopeKindRegistration(kind, parentKind, "Nexus.ProductCore (Layer 06)");
}
