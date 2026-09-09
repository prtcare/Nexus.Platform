using Nexus.Governance.Contracts;
using Xunit;

namespace Nexus.Governance.Tests;

public sealed class ProductSlugTests
{
    [Theory]
    [InlineData("product")]
    [InlineData("internal-tool")]
    [InlineData("a")]
    [InlineData("a1")]
    [InlineData("123")]
    [InlineData("product-2")]
    [InlineData("a-b-c")]
    public void ValidSlugs_AreAccepted(string slug)
    {
        var parsed = new ProductSlug(slug);

        Assert.Equal(slug, parsed.Value);
    }

    [Fact]
    public void MaxLength64Slug_IsAccepted()
    {
        var slug = new string('a', 64);

        _ = new ProductSlug(slug);
    }

    [Theory]
    [InlineData("my product")]
    [InlineData("MyProduct")]
    [InlineData("MYPRODUCT")]
    [InlineData("-product")]
    [InlineData("product-")]
    [InlineData("product--two")]
    [InlineData("my_product")]
    [InlineData("product!")]
    public void InvalidSlugs_AreRejected(string slug)
    {
        Assert.Throws<ArgumentException>(() => new ProductSlug(slug));
    }

    [Fact]
    public void EmptyNullOrWhitespace_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ProductSlug(""));
        Assert.Throws<ArgumentException>(() => new ProductSlug("   "));
        Assert.Throws<ArgumentException>(() => new ProductSlug(null!));
    }

    [Fact]
    public void SlugLongerThan64Chars_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ProductSlug(new string('a', 65)));
    }

    [Fact]
    public void Equality_IsByValue_AndToStringReturnsValue()
    {
        var slug1 = new ProductSlug("internal-tool");
        var slug2 = new ProductSlug("internal-tool");
        var slug3 = new ProductSlug("other-tool");

        Assert.Equal(slug1, slug2);
        Assert.True(slug1 == slug2);
        Assert.Equal(slug1.GetHashCode(), slug2.GetHashCode());
        Assert.NotEqual(slug1, slug3);
        Assert.Equal("internal-tool", slug1.ToString());
    }
}
