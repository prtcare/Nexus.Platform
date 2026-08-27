using System.Collections.Concurrent;
using Nexus.ProductCore.Contracts;

namespace Nexus.ProductCore.Scope.Registration;

/// <summary>
/// M-06-1.2 "Extensible scope registration". A plain lookup table - deliberately contains no
/// branch keyed on a specific consumer's identity, so a machine-domain consumer (or any
/// future product) can register an entirely different hierarchy without a code change here.
/// The three well-known trunk kinds are pre-registered so a consumer's first registration
/// can immediately declare a parent of "Subproject" without a chicken-and-egg ordering
/// requirement.
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
