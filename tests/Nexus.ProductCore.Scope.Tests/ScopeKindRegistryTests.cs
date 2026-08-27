using Nexus.ProductCore.Contracts;
using Nexus.ProductCore.Scope.Registration;
using Xunit;

namespace Nexus.ProductCore.Scope.Tests;

public sealed class ScopeKindRegistryTests
{
    [Fact]
    public void TrunkKinds_AreRegisteredByDefault()
    {
        var registry = new ScopeKindRegistry();

        Assert.True(registry.IsRegistered(WellKnownScopeKinds.Workspace));
        Assert.True(registry.IsRegistered(WellKnownScopeKinds.Project));
        Assert.True(registry.IsRegistered(WellKnownScopeKinds.Subproject));
        Assert.Equal(3, registry.All.Count);
    }

    [Fact]
    public void Register_ConsumerKindBelowSubproject_Succeeds()
    {
        // Mirrors what Nexus.Developer will do in Slice 3: register Feature (and Task,
        // Subtask) as scope kinds below the shared Subproject trunk kind.
        var registry = new ScopeKindRegistry();
        var feature = new ScopeKind("Feature");

        registry.Register(new ScopeKindRegistration(
            feature, WellKnownScopeKinds.Subproject, "Nexus.Developer"));

        Assert.True(registry.IsRegistered(feature));
    }

    [Fact]
    public void Register_EntirelyDifferentHierarchy_SucceedsWithoutLayer06CodeChange()
    {
        // Mirrors the M-06-1.2 acceptance criterion for a machine-domain consumer: its own
        // root kind, parented on nothing (a new root next to Workspace, not below it).
        var registry = new ScopeKindRegistry();
        var machine = new ScopeKind("Machine");

        registry.Register(new ScopeKindRegistration(machine, ParentKind: null, "Nexus.Machine"));

        Assert.True(registry.IsRegistered(machine));
    }

    [Fact]
    public void Register_DuplicateKind_Throws()
    {
        var registry = new ScopeKindRegistry();
        var feature = new ScopeKind("Feature");
        registry.Register(new ScopeKindRegistration(feature, WellKnownScopeKinds.Subproject, "Nexus.Developer"));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new ScopeKindRegistration(feature, WellKnownScopeKinds.Subproject, "SomeoneElse")));
    }

    [Fact]
    public void Register_UnregisteredParentKind_Throws()
    {
        var registry = new ScopeKindRegistry();
        var orphan = new ScopeKind("Orphan");
        var missingParent = new ScopeKind("DoesNotExist");

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new ScopeKindRegistration(orphan, missingParent, "Nexus.Developer")));
    }
}
