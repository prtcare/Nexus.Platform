using Microsoft.Extensions.DependencyInjection;
using Nexus.Governance.Contracts;
using Nexus.Governance.Core;
using Xunit;

namespace Nexus.Governance.Tests;

public sealed class ProductRegistryServiceTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private static IProductRegistry CreateRegistry()
        => new ServiceCollection()
            .AddGovernance()
            .BuildServiceProvider()
            .GetRequiredService<IProductRegistry>();

    [Fact]
    public async Task RegisterProduct_Success_ReturnsProductWithProposedLifecycle()
    {
        var registry = CreateRegistry();

        var result = await registry.RegisterProductAsync(
            TenantA, "Internal Tool", "internal-tool", ProductClassification.Internal);

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureCode);
        Assert.NotNull(result.Product);
        Assert.False(string.IsNullOrWhiteSpace(result.Product!.Id.Value));
        Assert.Equal(TenantA, result.Product.TenantId);
        Assert.Equal("Internal Tool", result.Product.Name);
        Assert.Equal("internal-tool", result.Product.Slug.Value);
        Assert.Equal(ProductClassification.Internal, result.Product.Classification);
        Assert.Equal(ProductLifecycleState.Proposed, result.Product.LifecycleState);
    }

    [Fact]
    public async Task RegisterProduct_DuplicateSlugSameTenant_ReturnsDuplicateSlug_AndStoresOneRow()
    {
        var registry = CreateRegistry();

        var first = await registry.RegisterProductAsync(
            TenantA, "Widget", "widget", ProductClassification.Consumer);
        var duplicate = await registry.RegisterProductAsync(
            TenantA, "Widget Again", "widget", ProductClassification.Business);

        Assert.True(first.Succeeded);
        Assert.False(duplicate.Succeeded);
        Assert.Null(duplicate.Product);
        Assert.Equal(RegisterProductResult.DuplicateSlug, duplicate.FailureCode);

        var products = await registry.ListProductsAsync(TenantA);
        Assert.Single(products);
        Assert.Equal("Widget", products[0].Name);
        Assert.Equal("widget", products[0].Slug.Value);
    }

    [Fact]
    public async Task RegisterProduct_SameSlugDifferentTenant_BothSucceed()
    {
        var registry = CreateRegistry();

        var inA = await registry.RegisterProductAsync(
            TenantA, "Widget", "widget", ProductClassification.Consumer);
        var inB = await registry.RegisterProductAsync(
            TenantB, "Widget", "widget", ProductClassification.Consumer);

        Assert.True(inA.Succeeded);
        Assert.True(inB.Succeeded);
        Assert.Single(await registry.ListProductsAsync(TenantA));
        Assert.Single(await registry.ListProductsAsync(TenantB));
    }

    [Fact]
    public async Task CrossTenantReadIsolation_ListEmptyAndGetNotFoundFromOtherTenant()
    {
        var registry = CreateRegistry();
        var registered = await registry.RegisterProductAsync(
            TenantA, "Secret", "secret", ProductClassification.Internal);
        var product = registered.Product!;

        Assert.Empty(await registry.ListProductsAsync(TenantB));

        var fromB = await registry.GetProductAsync(TenantB, product.Id);
        Assert.False(fromB.Found);
        Assert.Null(fromB.Product);

        var fromA = await registry.GetProductAsync(TenantA, product.Id);
        Assert.True(fromA.Found);
    }

    [Fact]
    public async Task GetProduct_ById_ReturnsNameSlugClassificationAndLifecycle()
    {
        var registry = CreateRegistry();
        var registered = await registry.RegisterProductAsync(
            TenantA, "Consumer App", "consumer-app", ProductClassification.Consumer);
        var product = registered.Product!;

        var result = await registry.GetProductAsync(TenantA, product.Id);

        Assert.True(result.Found);
        Assert.NotNull(result.Product);
        Assert.Equal(product.Id, result.Product!.Id);
        Assert.Equal(TenantA, result.Product.TenantId);
        Assert.Equal("Consumer App", result.Product.Name);
        Assert.Equal("consumer-app", result.Product.Slug.Value);
        Assert.Equal(ProductClassification.Consumer, result.Product.Classification);
        Assert.Equal(ProductLifecycleState.Proposed, result.Product.LifecycleState);
    }

    [Fact]
    public async Task GetProduct_UnknownId_ReturnsNotFound()
    {
        var registry = CreateRegistry();

        var result = await registry.GetProductAsync(TenantA, new ProductId("no-such-product"));

        Assert.False(result.Found);
        Assert.Null(result.Product);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RegisterProduct_BlankName_ReturnsInvalidName(string? name)
    {
        var registry = CreateRegistry();

        var result = await registry.RegisterProductAsync(
            TenantA, name!, "widget", ProductClassification.Internal);

        Assert.False(result.Succeeded);
        Assert.Null(result.Product);
        Assert.Equal(RegisterProductResult.InvalidName, result.FailureCode);
    }

    [Fact]
    public async Task RegisterProduct_BlankName_StoresNothing()
    {
        var registry = CreateRegistry();

        await registry.RegisterProductAsync(TenantA, "   ", "widget", ProductClassification.Internal);

        Assert.Empty(await registry.ListProductsAsync(TenantA));
    }
}
