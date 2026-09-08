using Nexus.Assurance.Contracts;
using Xunit;

namespace Nexus.Assurance.Tests;

public sealed class AssuranceEvidenceIdTests
{
    [Fact]
    public void Equality_IsByValue()
    {
        var id1 = new AssuranceEvidenceId("ev-20260907-0001");
        var id2 = new AssuranceEvidenceId("ev-20260907-0001");
        var id3 = new AssuranceEvidenceId("ev-20260907-0002");

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
        Assert.Throws<ArgumentException>(() => new AssuranceEvidenceId(value!));
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        Assert.Equal("ev-20260907-0001", new AssuranceEvidenceId("ev-20260907-0001").ToString());
    }
}
