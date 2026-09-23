using System.IO.Compression;
using System.Text;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// Environment-neutrality inspection. The property under test is one direction only — a finding proves an
/// environment dependence, while absence is meaningful evidence against one — so the tests cover both the
/// detection and its non-vacuity, in each of the two encodings a marker can hide in.
/// </summary>
public sealed class EnvironmentNeutralityInspectorTests : IDisposable
{
    private readonly string _root;

    public EnvironmentNeutralityInspectorTests()
        => _root = Path.Combine(Path.GetTempPath(), "nexus-w92-neutral-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Zip(string name, params (string Entry, byte[] Content)[] entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name + ".zip");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var e = archive.CreateEntry(entry);
            using var stream = e.Open();
            stream.Write(content);
        }

        return path;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Utf16(string text) => Encoding.Unicode.GetBytes(text);

    /// <summary>
    /// A .NET assembly stores its string literals as UTF-16 in the metadata heap, so a text decode would miss
    /// exactly the case this check exists for. The byte search tries both encodings, and this test is why.
    /// </summary>
    [Fact]
    public void AMarkerInUtf16_IsFound()
    {
        var artifact = Zip("utf16", ("app.dll", Utf16("...\0ENV-PROD\0...")));

        var result = EnvironmentNeutralityInspector.Inspect(artifact);

        Assert.False(result.IsNeutral);
        Assert.Contains("ENV-PROD", string.Join(" ", result.Findings), StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkerInUtf8_IsFound()
    {
        var artifact = Zip("utf8", ("appsettings.json", Utf8("""{ "environment": "ENV-TEST" }""")));

        var result = EnvironmentNeutralityInspector.Inspect(artifact);

        Assert.False(result.IsNeutral);
        Assert.Contains("ENV-TEST", string.Join(" ", result.Findings), StringComparison.Ordinal);
    }

    /// <summary>
    /// The non-vacuity control: the same file shape without a marker is reported neutral. Without this, a
    /// scanner that flagged everything would pass the two tests above.
    /// </summary>
    [Fact]
    public void AnArtifactWithNoMarker_IsNeutral()
    {
        var artifact = Zip(
            "clean",
            ("app.dll", Utf16("Program.Main")),
            ("appsettings.json", Utf8("""{ "ApiKeyRef": "NEXUS_PROVIDER_CREDENTIAL_REF" }""")));

        var result = EnvironmentNeutralityInspector.Inspect(artifact);

        Assert.True(result.IsNeutral);
        Assert.Empty(result.Findings);
        Assert.Equal(2, result.FilesInspected);
    }

    [Fact]
    public void AllThreeRatifiedIdentifiersAreDetected()
    {
        var artifact = Zip(
            "all",
            ("a.json", Utf8("ENV-DEV")),
            ("b.json", Utf8("ENV-TEST")),
            ("c.json", Utf8("ENV-PROD")));

        var result = EnvironmentNeutralityInspector.Inspect(artifact);

        Assert.False(result.IsNeutral);
        Assert.Equal(3, result.Findings.Count);
    }

    [Fact]
    public void ACallerSuppliedMarkerIsDetected()
    {
        var artifact = Zip("endpoint", ("runtime.json", Utf8("""{ "api": "https://api.test.example" }""")));

        var result = EnvironmentNeutralityInspector.Inspect(artifact, ["api.test.example"]);

        Assert.False(result.IsNeutral);
        Assert.Contains("api.test.example", string.Join(" ", result.Findings), StringComparison.Ordinal);
    }

    /// <summary>
    /// A marker that cannot be present is not reported, so the inspector is not reporting on everything.
    /// </summary>
    [Fact]
    public void AMarkerThatCannotBePresent_IsNotReported()
    {
        var absent = "ENV-ABSENT-" + Guid.NewGuid().ToString("N");
        var artifact = Zip("absent", ("a.json", Utf8("nothing to see")));

        var result = EnvironmentNeutralityInspector.Inspect(artifact, [absent]);

        Assert.True(result.IsNeutral);
    }

    [Fact]
    public void NonTextEntries_AreSkippedAndReportedAsSuch()
    {
        var artifact = Zip("binary", ("image.png", [0x89, 0x50, 0x4E, 0x47]), ("a.json", Utf8("clean")));

        var result = EnvironmentNeutralityInspector.Inspect(artifact);

        Assert.True(result.IsNeutral);
        Assert.Equal(1, result.FilesInspected);
        Assert.Contains("image.png", result.SkippedFiles);
    }

    [Fact]
    public void AMissingArtifact_IsNotNeutral()
    {
        // Something to inspect that does not exist cannot be established as neutral.
        var result = EnvironmentNeutralityInspector.Inspect(Path.Combine(_root, "absent.zip"));

        Assert.False(result.IsNeutral);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public void Findings_NameTheEntryAndOffset_WithoutDumpingContent()
    {
        var artifact = Zip("located", ("sub/app.config", Utf8("x".PadRight(20, 'x') + "ENV-PROD")));

        var finding = Assert.Single(EnvironmentNeutralityInspector.Inspect(artifact).Findings);

        Assert.Contains("sub/app.config", finding, StringComparison.Ordinal);
        Assert.Contains("byte offset", finding, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxxxxxxxx", finding, StringComparison.Ordinal);
    }
}
