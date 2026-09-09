using Nexus.ProductCore.Contracts;
using Nexus.ProductCore.Scope.Common.Identifiers;
using DomainProject = Nexus.ProductCore.Scope.Project.Project;
using ProjectStatus = Nexus.ProductCore.Scope.Project.ProjectStatus;
using Xunit;

namespace Nexus.ProductCore.Scope.Tests;

public sealed class ProjectTests
{
    [Fact]
    public void Create_TrimsName_AndDefaultsToActiveWithEmptyReference()
    {
        var workspaceId = WorkspaceId.New();

        var project = new DomainProject(
            ProjectId.New(), workspaceId, "  My Project  ", DateTimeOffset.UtcNow);

        Assert.Equal("My Project", project.Name);
        Assert.Equal(workspaceId, project.WorkspaceId);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(string.Empty, project.Reference);
    }

    [Fact]
    public void Restore_RehydratesReference()
    {
        var id = ProjectId.New();
        var workspaceId = WorkspaceId.New();

        var project = DomainProject.Restore(
            id, workspaceId, "P", ProjectStatus.Archived, DateTimeOffset.UtcNow, "PRJ-00000001");

        Assert.Equal("PRJ-00000001", project.Reference);
        Assert.Equal(ProjectStatus.Archived, project.Status);
    }

    [Fact]
    public void ImplementsIScopeNode_WithWorkspaceAsParent()
    {
        var workspaceId = WorkspaceId.New();
        var project = new DomainProject(
            ProjectId.New(), workspaceId, "P", DateTimeOffset.UtcNow);

        IScopeNode node = project;

        Assert.Equal(WellKnownScopeKinds.Project, node.Kind);
        Assert.Equal(workspaceId.Value, node.ParentId);
        Assert.Equal("P", node.DisplayName);
    }
}
