using Nexus.ProductCore.Contracts;
using Nexus.ProductCore.Scope.Common.Identifiers;
using DomainWorkspace = Nexus.ProductCore.Scope.Workspace.Workspace;
using WorkspaceStatus = Nexus.ProductCore.Scope.Workspace.WorkspaceStatus;
using Xunit;

namespace Nexus.ProductCore.Scope.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public void Create_TrimsNameOwnerAndDescription_AndDefaultsToActive()
    {
        var workspace = new DomainWorkspace(
            WorkspaceId.New(),
            "  My Workspace  ",
            "  Durai  ",
            "  a description  ",
            DateTimeOffset.UtcNow);

        Assert.Equal("My Workspace", workspace.Name);
        Assert.Equal("Durai", workspace.Owner);
        Assert.Equal("a description", workspace.Description);
        Assert.Equal(WorkspaceStatus.Active, workspace.Status);
        Assert.Equal(string.Empty, workspace.Reference);
    }

    [Fact]
    public void Create_ThrowsOnBlankName()
    {
        Assert.Throws<ArgumentException>(() =>
            new DomainWorkspace(WorkspaceId.New(), "   ", "Durai", "", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Archive_SetsStatusToArchived()
    {
        var workspace = new DomainWorkspace(
            WorkspaceId.New(), "W", "Owner", "", DateTimeOffset.UtcNow);

        workspace.Archive();

        Assert.Equal(WorkspaceStatus.Archived, workspace.Status);
    }

    [Fact]
    public void Restore_RehydratesReferenceWithoutRunningCreateLogic()
    {
        var id = WorkspaceId.New();
        var createdAt = DateTimeOffset.UtcNow;

        var workspace = DomainWorkspace.Restore(
            id, "W", "Owner", "desc", WorkspaceStatus.Archived, createdAt, "WKS-00000001");

        Assert.Equal("WKS-00000001", workspace.Reference);
        Assert.Equal(WorkspaceStatus.Archived, workspace.Status);
        Assert.Equal(id, workspace.Id);
    }

    [Fact]
    public void ImplementsIScopeNode_AsARootWithNoParent()
    {
        var workspace = new DomainWorkspace(
            WorkspaceId.New(), "W", "Owner", "", DateTimeOffset.UtcNow);

        IScopeNode node = workspace;

        Assert.Equal(workspace.Id.Value, node.Id);
        Assert.Equal(WellKnownScopeKinds.Workspace, node.Kind);
        Assert.Null(node.ParentId);
        Assert.Equal("W", node.DisplayName);
    }
}
