using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// Calibration regressions found by RUNNING the gate against real repositories, not by imagining cases.
///
/// <para>
/// W9.0 warned that a scan which cries wolf is muted within a week, and listed the five false positives it
/// had measured. Every test below is a false positive the scanner actually produced afterwards — during the
/// W9.2 first proof run, against MarketSurvey's own documentation — and each one cost a refused
/// certification until it was fixed. They are kept here so the next change to the shape rules has to keep
/// them clean.
/// </para>
/// </summary>
public sealed class ScannerCalibrationRegressionTests
{
    private readonly SecretScanner _scanner = new();

    /// <summary>
    /// Measured: three documentation lines in MarketSurvey (`docs/DEPLOYMENT.md:17`,
    /// `docs/prt-s02/ENVIRONMENT-MODEL.md:153` and `:279`) elide a connection-string password as `...`, and
    /// the connection-string rule reported each one as a credential — because that rule consulted
    /// placeholders and indirections but not redaction markers, while the value path consulted all three.
    ///
    /// <para>
    /// The fix was not to special-case an ellipsis; it was to make BOTH paths call one predicate
    /// (<see cref="CredentialShape.IsNonValueLiteral"/>). Two implementations of one judgement is one
    /// implementation too many, and these is the divergence it produced.
    /// </para>
    /// </summary>
    [Fact]
    public void AConnectionStringWithAnElidedPassword_IsNotReported()
    {
        const string line =
            "| `ConnectionStrings__Database` | `Host=...;Database=...;Username=...;Password=...;SSL Mode=VerifyFull` |";

        Assert.Empty(_scanner.ScanText("docs/DEPLOYMENT.md", line));
    }

    [Fact]
    public void AConnectionStringWithAMaskedPassword_IsNotReported()
    {
        const string line = """$env:ConnectionStrings__Database = "Host=localhost;Database=market_survey;Username=survey;Password=<masked>" """;

        Assert.Empty(_scanner.ScanText("docs/prt-s02/ENVIRONMENT-MODEL.md", line));
    }

    [Fact]
    public void AConnectionStringWithARedactionWord_IsNotReported()
    {
        var marker = string.Concat("Pass", "word=");
        var line = "ConnectionStrings__Database=Host=db;Username=survey;" + marker + "[REDACTED]";

        Assert.Empty(_scanner.ScanText("docs/ops.md", line));
    }

    /// <summary>
    /// The control that keeps the three above honest: the same line shape with a value that is NOT elided
    /// must still be reported. Without this, a rule that ignored every password would pass them all.
    /// </summary>
    [Fact]
    public void AConnectionStringWithARealLookingPassword_IsStillReported()
    {
        var marker = string.Concat("Pass", "word=");
        var line = "ConnectionStrings__Database=Host=db;Username=survey;" + marker + TestData.RandomAlnum(20);

        Assert.NotEmpty(_scanner.ScanText("appsettings.json", line));
    }

    /// <summary>
    /// An elision is not the only documentation form. A short value after a password marker is far more
    /// likely to be an example than a credential, and the connection-string rule is only reached for a line
    /// that also has connection-string structure — so the cost of treating an obviously-short value as an
    /// example is low, while the cost of reporting it is a certification refused for a table.
    /// </summary>
    [Theory]
    [InlineData("...")]
    [InlineData("<masked>")]
    [InlineData("[REDACTED]")]
    [InlineData("xxx")]
    [InlineData("***")]
    [InlineData("changeme")]
    [InlineData("your-password-here")]
    [InlineData("$DB_PASSWORD")]
    [InlineData("${DB_PASSWORD}")]
    public void DocumentedElisionForms_AreAllNonValueLiterals(string value)
    {
        Assert.True(CredentialShape.IsNonValueLiteral(value), $"'{value}' should be recognised as a non-value literal.");
    }

    /// <summary>
    /// And the other direction, so the predicate is not vacuously true: values that are real candidates are
    /// not swept up by it.
    /// </summary>
    [Theory]
    [InlineData("Xk9Qm2Vt7Lp4Rb8Nzt3Hy6Wb1")]
    [InlineData("sk-deadbeefdeadbeefdeadbeefdeadbeef")]
    public void RealLookingValues_AreNotNonValueLiterals(string value)
    {
        Assert.False(CredentialShape.IsNonValueLiteral(value), $"'{value}' should NOT be excluded as a non-value literal.");
        Assert.True(CredentialShape.LooksLikeCredentialValue(value));
    }
}
