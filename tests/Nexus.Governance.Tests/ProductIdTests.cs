using Nexus.Governance.Contracts;
using Xunit;

namespace Nexus.Governance.Tests;

public sealed class ProductIdTests
{
    [Fact]
    public void Equality_IsByValue()
    {
        var id1 = new ProductId("a1b2c3d4");
        var id2 = new ProductId("a1b2c3d4");
        var id3 = new ProductId("different");

        Assert.Equal(id1, id2);
        Assert.True(id1 == id2);
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
        Assert.NotEqual(id1, id3);
        Assert.False(id1 == id3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespace_IsRejected(string? value)
    {
        Assert.Throws<ArgumentException>(() => new ProductId(value!));
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        Assert.Equal("abc123", new ProductId("abc123").ToString());
    }
}
