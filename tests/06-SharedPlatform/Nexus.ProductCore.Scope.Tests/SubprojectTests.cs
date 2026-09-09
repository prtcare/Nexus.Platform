using Nexus.ProductCore.Contracts;
using Nexus.ProductCore.Scope.Common.Identifiers;
using DomainSubproject = Nexus.ProductCore.Scope.Subproject.Subproject;
using SubprojectStatus = Nexus.ProductCore.Scope.Subproject.SubprojectStatus;
using Xunit;

namespace Nexus.ProductCore.Scope.Tests;

public sealed class SubprojectTests
{
    [Fact]
    public void Create_TrimsNameAndDescription_AndDefaultsToActive()
    {
        var projectId = ProjectId.New();

        var subproject = new DomainSubproject(
            SubprojectId.New(), projectId, "  Sub  ", "  desc  ", DateTimeOffset.UtcNow);

        Assert.Equal("Sub", subproject.Name);
        Assert.Equal("desc", subproject.Description);
        Assert.Equal(projectId, subproject.ProjectId);
        Assert.Equal(SubprojectStatus.Active, subproject.Status);
        Assert.Equal(string.Empty, subproject.Reference);
    }

    [Fact]
    public void Restore_RehydratesReference()
    {
        var id = SubprojectId.New();
        var projectId = ProjectId.New();

        var subproject = DomainSubproject.Restore(
            id, projectId, "S", "d", SubprojectStatus.Archived, DateTimeOffset.UtcNow, "SPR-00000001");

        Assert.Equal("SPR-00000001", subproject.Reference);
        Assert.Equal(SubprojectStatus.Archived, subproject.Status);
    }

    [Fact]
    public void ImplementsIScopeNode_WithProjectAsParent()
    {
        var projectId = ProjectId.New();
        var subproject = new DomainSubproject(
            SubprojectId.New(), projectId, "S", "", DateTimeOffset.UtcNow);

        IScopeNode node = subproject;

        Assert.Equal(WellKnownScopeKinds.Subproject, node.Kind);
        Assert.Equal(projectId.Value, node.ParentId);
        Assert.Equal("S", node.DisplayName);
    }
}
