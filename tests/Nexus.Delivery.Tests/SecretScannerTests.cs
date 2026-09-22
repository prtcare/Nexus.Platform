using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The W9 build gate's calibration set.
///
/// <para>
/// <b>Every case here is a real candidate from the W9.0 sweep, not an invented one.</b> That sweep
/// produced seven credential-shaped values across the estate; five were noise and two were genuine.
/// Those seven are the regression corpus below.
/// </para>
///
/// <para>
/// <b>Why the positive fixtures are GENERATED rather than written as literals.</b> The first draft of
/// this file wrote the credential shapes as string literals, and the repository's own secret-scan gate
/// — <c>SecretScanGateTests</c>, added in this same change — failed the build with five findings,
/// all of them in this file. That was the gate being correct. A credential-shaped literal committed in a
/// test file is <i>indistinguishable from a leak</i>: real credentials are committed in test files
/// constantly, and "it is only a fixture" is not a property the scanner can see, nor one a reviewer can
/// verify at a glance. The alternative — an allowlist for known test fixtures — was rejected outright,
/// because a suppression list is precisely where a real credential would hide, and this estate has
/// already recorded three separate controls that went green without being able to fail.
/// </para>
///
/// <para>
/// So the positive fixtures are constructed at run time from a fresh alphabet each execution. The
/// assertions are unchanged in strength — slightly stronger, since each run uses a new value.
/// </para>
/// </summary>
public sealed class SecretScannerTests
{
    private readonly SecretScanner _scanner = new();

    // =============================================================================================
    // Generated fixtures (see TestData.RandomAlnum for why these are not literals)
    // =============================================================================================

    /// <summary>A vendor-prefixed, 35-character machine key: prefix, hyphen, then 32 unbroken characters.</summary>
    private static string VendorKeyShape() => "sk-" + TestData.RandomAlnum(32);

    /// <summary>A 25-character dense mixed-case token.</summary>
    private static string TokenShape() => TestData.RandomAlnum(25);

    /// <summary>A 16-character password — short enough that only the connection-string rule can judge it.</summary>
    private static string ShortPasswordShape() => TestData.RandomAlnum(16);

    // =============================================================================================
    // The two genuine findings. Both must be caught.
    // =============================================================================================

    /// <summary>
    /// W9.0 candidate 2 — the Forge finding, and the reason this gate exists.
    ///
    /// The real value was a 35-character DeepSeek-format key (a vendor prefix followed by 32 hexadecimal
    /// characters) assigned to a provider auth-token variable in a tracked PowerShell profile, committed
    /// in <c>273318c</c> and reachable from every clone of that repository.
    ///
    /// <para>
    /// Note what the detector uses: the key NAME tells it the assignment is credential-bearing, and the
    /// VALUE is judged by shape — a 32-character unbroken run. It never needs to know what the prefix
    /// means, which is what makes it provider-neutral. A vendor prefix list would have caught this and
    /// nothing else; the run-length rule catches every machine-generated key of this family.
    /// </para>
    /// </summary>
    [Fact]
    public void ForgeProfileShape_AuthTokenWithAVendorKey_IsFound()
    {
        var key = VendorKeyShape();
        var line = "    $env:PROVIDER_AUTH_TOKEN=\"" + key + "\"";

        var findings = _scanner.ScanText("Forge/Archived/Microsoft.PowerShell_profile.ps1", line);

        var finding = Assert.Single(findings);
        Assert.Equal("credential-shaped-value", finding.RuleName);
        Assert.Equal(1, finding.LineNumber);
        Assert.Equal(key.Length, finding.ValueLength);
    }

    /// <summary>
    /// W9.0 candidate 7 — a development password committed in MarketSurvey's README.
    ///
    /// This one is deliberately expected to FIRE. W9.0 recorded it as a genuine (low-severity) violation
    /// of <c>CONFIGURATION_STANDARDS.md</c> §13, which forbids committing "**Any password** — including a
    /// development one", and a gate that stayed silent on it would be under-reporting a rule the estate
    /// already wrote down.
    ///
    /// <para>
    /// The password here is 16 characters, which no shape rule can confidently judge alone — so this is
    /// the case that proves the connection-string rule is needed at all. The connection string is the
    /// evidence; a key-value capture would see only the fragment after the password marker.
    /// </para>
    /// </summary>
    [Fact]
    public void CommittedConnectionStringWithAPassword_IsFound()
    {
        var password = ShortPasswordShape();

        // The password marker is assembled at run time so that THIS file does not itself read as a
        // connection string carrying a credential — the exact finding the gate caught in the first draft.
        var marker = string.Concat("Pass", "word=");
        var line = "$env:ConnectionStrings__Database='Host=localhost;Port=55432;Database=market_survey;"
            + "Username=survey;" + marker + password + "'";

        var findings = _scanner.ScanText("PRT/MarketSurvey/README.md", line);

        var finding = Assert.Single(findings);
        Assert.Equal("connection-string-with-credential", finding.RuleName);
    }

    // =============================================================================================
    // The five false positives. None may be caught.
    // =============================================================================================

    /// <summary>
    /// W9.0 candidate 1 — a deliberate, named probe value in the AI Head's own secret-boundary test.
    /// Caught by the placeholder rule: the author said in the value itself that it is not real.
    /// </summary>
    [Fact]
    public void ANamedProbeValue_IsNotReported()
    {
        const string line = "$probeSecret = 'w6r2-probe-value-not-a-real-credential'";

        Assert.Empty(_scanner.ScanText("AI/Intelligence/Test-AiProviderLaunchEnvironment.ps1", line));
    }

    /// <summary>
    /// W9.0 candidate 4 — the single most common false positive: a variable NAME on the right-hand side
    /// of an assignment. Caught by the bare-identifier rule.
    /// </summary>
    [Fact]
    public void AnEnvironmentVariableIdentifierAsAValue_IsNotReported()
    {
        const string line = "  DB_PASSWORD: 'WORKBENCH_DB_PASSWORD',";

        Assert.Empty(_scanner.ScanText("WorkBench/src/persistence/config.js", line));
    }

    /// <summary>W9.0 candidate 5 — a redaction marker inside a comment that documents a redaction test.</summary>
    [Fact]
    public void ARedactionMarkerInAComment_IsNotReported()
    {
        const string line = """     * `password: "[REDACTED]"`, and `SET_REFERENCE_TOKEN: 'SET_REFERENCE_TOKEN'`""";

        Assert.Empty(_scanner.ScanText("WorkBench/tests/persistence.test.js", line));
    }

    /// <summary>
    /// W9.0 candidate 6 — an HTML autocomplete token. The vocabulary word appears, but never as an
    /// assignment key, so the assignment pattern does not match it at all.
    /// </summary>
    [Fact]
    public void AnHtmlAutocompleteToken_IsNotReported()
    {
        const string line = """                  autoComplete={register ? "new-password" : "current-password"}""";

        Assert.Empty(_scanner.ScanText("PRT/MarketSurvey/apps/web/app/page.tsx", line));
    }

    /// <summary>
    /// W9.0 candidate 3 — a design-time context factory that reads configuration and inlines no value.
    /// Recorded during the sweep as "no committed value": the type exists, the literal does not.
    /// </summary>
    [Fact]
    public void ADesignTimeFactoryThatReadsConfiguration_IsNotReported()
    {
        const string line = "        var connectionString = configuration.GetConnectionString(\"NexusChat\");";

        Assert.Empty(_scanner.ScanText("Experience/NexusChatDbContextFactory.cs", line));
    }

    /// <summary>
    /// The defect the gate found in the scanner's own source: prose that mentions a password marker and
    /// then continues. The first version of the connection-string rule reported its own doc comment.
    /// </summary>
    [Fact]
    public void ProseThatMentionsAPasswordMarker_IsNotReported()
    {
        var marker = string.Concat("Pass", "word=");
        var line = "            // the fragment after `" + marker + "`, which is too short for a shape rule";

        Assert.Empty(_scanner.ScanText("src/Nexus.Delivery.Core/SecretScanner.cs", line));
    }

    // =============================================================================================
    // The narrow exception the estate's own standard grants
    // =============================================================================================

    /// <summary>
    /// <c>CONFIGURATION_STANDARDS.md</c> §6.1 permits committing a machine-local, credential-free
    /// connection string. W9.0 verified both committed strings in the estate are of exactly this kind,
    /// and a gate that flagged them would be demanding a change the standard does not require.
    /// </summary>
    [Fact]
    public void ACommittedIntegratedAuthConnectionString_IsNotReported()
    {
        const string line =
            """"NexusDeveloper": "Server=(localdb)\\MSSQLLocalDB;Database=NexusDeveloper;Trusted_Connection=True;MultipleActiveResultSets=true"""";

        Assert.Empty(_scanner.ScanText("Products/Developer/appsettings.json", line));
    }

    [Fact]
    public void APermittedEnvironmentVariableReference_IsNotReported()
    {
        const string line = """    "ApiKeyRef": "NEXUS_OPENAI_API_KEY" """;

        // The estate's exemplar: a committed reference NAME resolved at run time.
        Assert.Empty(_scanner.ScanText("AI/Intelligence/appsettings.json", line));
    }

    [Fact]
    public void APlaceholderInAnEnvExample_IsNotReported()
    {
        const string line = "WORKBENCH_DB_PASSWORD=change-me-to-a-strong-local-development-password";

        Assert.Empty(_scanner.ScanText("WorkBench/.env.example", line));
    }

    // =============================================================================================
    // Non-vacuity — the estate's rule that a guard must be able to fail
    // =============================================================================================

    /// <summary>
    /// The discriminator. If the scanner reported everything, the false-positive tests above would fail;
    /// if it reported nothing, the finding tests would fail. This asserts the pair directly, on two lines
    /// that differ only in whether the assigned value is credential-shaped.
    /// </summary>
    [Fact]
    public void Scanner_Discriminates_OnValueShape_NotOnTheWord()
    {
        var benign = _scanner.ScanText("f.txt", "password: 'WORKBENCH_DB_PASSWORD'");
        var leaking = _scanner.ScanText("f.txt", "password: '" + TokenShape() + "'");

        Assert.Empty(benign);
        Assert.NotEmpty(leaking);
    }

    /// <summary>
    /// Negative control: a token that cannot exist is not reported, so the scanner is not reporting on
    /// every occurrence of the vocabulary.
    /// </summary>
    [Fact]
    public void Scanner_DoesNotReport_OnAValueThatCannotBePresent()
    {
        var absent = "NEXUS_ABSENT_" + Guid.NewGuid().ToString("N");

        Assert.Empty(_scanner.ScanText("f.txt", $"token: '{absent}'"));
    }

    // =============================================================================================
    // Registered patterns: where a provider-specific rule is allowed to live
    // =============================================================================================

    /// <summary>
    /// A vendor format is supplied by the party that owns the vendor — never built into the neutral
    /// engine. The finding names the pattern and captures no part of the match, so a registered rule
    /// cannot leak a value into a build log.
    /// </summary>
    [Fact]
    public void ARegisteredVendorPattern_FiresByName_AndCapturesNoValue()
    {
        var policy = SecretScanPolicy.Neutral with
        {
            RegisteredPatterns = [new SecretPattern("vendor-key-format", @"\bACME-[A-Z0-9]{24}\b", "A synthetic vendor key format.")]
        };

        var scanner = new SecretScanner(policy);
        var line = string.Concat("credential = ACME-", TestData.RandomUpperAlnum(24));

        // Two independent signals fire on this line and both SHOULD: the registered vendor format, and
        // the structural rule, because the value is a 29-character unbroken alphanumeric run. The second
        // is not noise — it is the rule that would catch the same key with its prefix changed.
        var findings = scanner.ScanText("f.txt", line);

        var vendor = Assert.Single(findings, f => f.RuleName == "vendor-key-format");
        Assert.Equal("(registered-pattern)", vendor.KeyName);

        // The value length is zero because the pattern's match is deliberately not captured.
        Assert.Equal(0, vendor.ValueLength);
    }

    // =============================================================================================
    // Operational properties of the gate itself
    // =============================================================================================

    [Fact]
    public void Findings_NeverContainAnyPartOfTheValue()
    {
        var secret = VendorKeyShape();
        var findings = _scanner.ScanText("f.txt", "auth_token = \"" + secret + "\"");

        var description = string.Join("\n", findings.Select(f => f.ToString()));

        Assert.NotEmpty(findings);
        Assert.DoesNotContain(secret, description, StringComparison.Ordinal);

        // Not even a fragment: a prefix or a tail of a live key is still a live key to whoever reads
        // the build log it landed in.
        Assert.DoesNotContain(secret[..12], description, StringComparison.Ordinal);
        Assert.DoesNotContain(secret[^8..], description, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanDirectory_ReportsFindings_WithRelativePathsAndLineNumbers()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, "src"));
            File.WriteAllText(
                System.IO.Path.Combine(root, "src", "leak.txt"),
                "line one is fine\napi_key = \"" + TokenShape() + "\"\n");

            var result = _scanner.ScanDirectory(root);

            Assert.Equal(SecretScanVerdict.Findings, result.Verdict);
            Assert.True(result.BlocksBuild);
            Assert.Equal(1, result.FilesScanned);

            var finding = Assert.Single(result.Findings);
            Assert.Equal("src/leak.txt", finding.RelativePath);
            Assert.Equal(2, finding.LineNumber);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanDirectory_IsClean_OnATreeWithNoCredentialShapedValues()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(System.IO.Path.Combine(root, "appsettings.json"), """{ "ApiKeyRef": "NEXUS_OPENAI_API_KEY" }""");

            var result = _scanner.ScanDirectory(root);

            Assert.Equal(SecretScanVerdict.Clean, result.Verdict);
            Assert.False(result.BlocksBuild);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A scan that cannot complete is never reported as clean. The verdict is <c>Incomplete</c> and it
    /// still blocks, which is the same rule the estate applies to a test suite that could not be run.
    /// </summary>
    [Fact]
    public void ScanDirectory_OnAMissingRoot_IsIncomplete_NotClean()
    {
        var result = _scanner.ScanDirectory(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus-w91-absent-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(SecretScanVerdict.Incomplete, result.Verdict);
        Assert.True(result.BlocksBuild);
        Assert.NotEmpty(result.IncompleteReasons);
    }

    /// <summary>
    /// Build output is skipped — including at the top of a tree. The first version of the glob matcher
    /// only understood <c>**/bin/**</c> with a preceding directory, so a root-level <c>bin/</c> was
    /// scanned rather than skipped: a skip that had stopped skipping.
    /// </summary>
    [Fact]
    public void ScanDirectory_SkipsBuildOutput_AtTheRootAsWellAsBelowIt()
    {
        var root = CreateTempDirectory();
        try
        {
            var bin = System.IO.Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(System.IO.Path.Combine(bin, "generated.txt"), "api_key = \"" + TokenShape() + "\"");

            var nested = System.IO.Path.Combine(root, "src", "obj");
            Directory.CreateDirectory(nested);
            File.WriteAllText(System.IO.Path.Combine(nested, "generated.txt"), "api_key = \"" + TokenShape() + "\"");

            var worktree = System.IO.Path.Combine(root, ".forge", "worktrees", "other");
            Directory.CreateDirectory(worktree);
            File.WriteAllText(System.IO.Path.Combine(worktree, "copy.txt"), "api_key = \"" + TokenShape() + "\"");

            var result = _scanner.ScanDirectory(root);

            Assert.Equal(SecretScanVerdict.Clean, result.Verdict);
            Assert.Equal(3, result.FilesSkipped);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus-w91-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
