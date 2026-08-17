using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Nexus.Platform.Architecture.Tests;

public sealed class PlatformBoundaryTests
{
    private static readonly Assembly[] PlatformAssemblies =
    [
        typeof(Nexus.Platform.Contracts.Models.ModelDescriptor).Assembly,
        typeof(Nexus.Platform.Core.PlatformServiceCollectionExtensions).Assembly,
        typeof(Nexus.Platform.Providers.OpenAI.OpenAIModelGateway).Assembly
    ];

    private static readonly string[] ForbiddenProductTypeNames =
    [
        "Workspace", "Project", "Conversation", "ConversationMessage", "Knowledge",
        "WorkItem", "Artifact", "Branch", "Snapshot", "Session", "Adr"
    ];

    [Fact]
    public void Platform_MustNotReference_IntelligenceOrProducts()
    {
        foreach (var assembly in PlatformAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Nexus.Intelligence", "Nexus.Products")
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{assembly.GetName().Name} has a forbidden dependency: " +
                string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void Platform_MustNotContain_ProductTypeNames()
    {
        var offenders = PlatformAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => ForbiddenProductTypeNames.Contains(t.Name))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, $"Forbidden product type names found: {string.Join(", ", offenders)}");
    }
}
