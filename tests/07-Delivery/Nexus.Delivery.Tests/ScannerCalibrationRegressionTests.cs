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

        // Built to be value-shaped by construction, not drawn at random: this fixture used
        // `TestData.RandomAlnum(20)` and was therefore a NAME — and so correctly not reported — about 3.0% of
        // runs. See TestData.ValueShapedToken.
        var line = "ConnectionStrings__Database=Host=db;Username=survey;" + marker + TestData.ValueShapedToken(20);

        Assert.NotEmpty(_scanner.ScanText("appsettings.json", line));
    }

    /// <summary>
    /// The false positive the W9 build gate produced against **this repository's own source** — found by
    /// running the gate, not by reading it.
    ///
    /// <para>
    /// <c>SecretScanGateTests.Repository_ContainsNoCommittedSecret</c> reported
    /// <c>src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs:139</c> as
    /// <c>rule=credential-shaped-value key=auth valueLen=43</c>. The line is
    /// <c>var auth = WorkbookCompatibilityReader.AuthorizeWrite(</c> — an ordinary method-call expression
    /// with no credential in it.
    /// </para>
    ///
    /// <para>
    /// <b>Mechanism.</b> The key vocabulary contains <c>auth</c>, so <c>auth =</c> opens a match, and the
    /// bare-value branch captures the rest of the line: <c>WorkbookCompatibilityReader.AuthorizeWrite(</c>.
    /// The captured text holds a 28-character unbroken alphanumeric run (<c>WorkbookCompatibilityReader</c>),
    /// which is past <c>CredentialRunThreshold</c>, so Branch A of
    /// <see cref="CredentialShape.LooksLikeCredentialValue"/> fires. Branch A is evaluated <i>before</i> the
    /// whitespace/prose exclusion, and none of the existing non-value exclusions describe this text: it is
    /// not a placeholder, not an indirection, not a redaction marker, and not a bare identifier — it is a
    /// dotted member-access chain, which nothing classified.
    /// </para>
    ///
    /// <para>
    /// <b>The fixture is verbatim line 139, indentation included.</b> It is written as a raw string literal
    /// so that the line this file contains is the line the scanner must judge, unattributed to any
    /// surrounding quoting. That the same text is flagged in a file with no <c>auth</c> assignment at all
    /// is itself the evidence that the rule, not the line, was wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void AnOrdinarySourceCodeMemberAccessChain_IsNotReported()
    {
        const string line = """        var auth = WorkbookCompatibilityReader.AuthorizeWrite(""";

        var findings = _scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", line);

        Assert.Empty(findings);
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

    // =============================================================================================
    // W9.4 — the canonical code-reference rule.
    //
    // The false positive above was answered by teaching the shape rules to tell a reference to CODE from a
    // VALUE. A rule added to a secret gate is a place detection can be lost from, so the controls for it are
    // stated in both directions, and the near misses are the ones that matter: a dotted token is exactly what
    // the rule recognises, so a dotted SECRET is the case a careless version would lose.
    //
    // W9.4 unified two independently-written fixes for this same false positive, and adopted neither whole.
    // One had the structural per-segment test (a segment that is not a name means a generated token) but no
    // notion of WHERE the text was captured, so the same characters between quotes were exempted too. The
    // other had the capture position and read only the reference rather than the whole run, but accepted ANY
    // identifier-shaped segment, so an unquoted JSON web token — which is exactly a dotted chain of long
    // alphabetic segments — was read as a member access and lost. Both properties are required. Both are
    // below, and the discriminator that forces the position to be an input is
    // `TheSameTextQuoted_IsStillJudgedAsAValue`.
    // =============================================================================================

    /// <summary>
    /// The exact text the scanner captures from line 139 — what follows <c>auth =</c> after the surrounding
    /// whitespace is consumed by the assignment pattern. Asserted on the predicate, not only through the
    /// scanner, so that unwiring the clause from <see cref="CredentialShape.IsNonValueLiteral"/> — the one
    /// place the judgement is made — fails a test rather than merely failing to fail.
    /// </summary>
    [Fact]
    public void TheCodeReferenceJudgement_IsMadeInTheOnePlace()
    {
        const string captured = "WorkbookCompatibilityReader.AuthorizeWrite(";

        Assert.True(CredentialShape.IsCodeReferenceShape(captured));

        // Both positions are asserted, so the clause cannot be dropped from IsNonValueLiteral unnoticed and
        // cannot be applied to a value unnoticed either. A clause wired into the scanner but not into the
        // one predicate would pass the scanner tests above and fail here — which is the point of asserting
        // it here at all.
        Assert.True(CredentialShape.IsNonValueLiteral(captured, CredentialCapturePosition.UnquotedAssignmentOperand));
        Assert.False(CredentialShape.IsNonValueLiteral(captured, CredentialCapturePosition.Value));

        Assert.False(CredentialShape.LooksLikeCredentialValue(captured, CredentialCapturePosition.UnquotedAssignmentOperand));
    }

    /// <summary>
    /// The same statement with arguments, which is what the pattern actually captures: the bare-value branch
    /// stops at the first whitespace, so the shape rule sees
    /// <c>WorkbookCompatibilityReader.AuthorizeWrite(read,</c> — a chain truncated mid-call. The rule must
    /// judge the reference, not the call site, and must leave the remainder to be judged as text.
    /// </summary>
    [Fact]
    public void ASourceCodeCallWithAnArgumentList_IsNotReported()
    {
        const string line =
            """        var auth = WorkbookCompatibilityReader.AuthorizeWrite(read, FormMap.ToImplementation(intendedForm));""";

        Assert.Empty(_scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", line));
    }

    /// <summary>
    /// The other half of the same statement, and the reason the reference is read as a <b>length</b> rather
    /// than judged as a whole. The capture here is
    /// <c>WorkbookCompatibilityReader.AuthorizeWrite(password:&lt;token&gt;)</c>. Recognising the call must
    /// consume the reference <c>WorkbookCompatibilityReader.AuthorizeWrite(</c> and nothing else — what
    /// remains is an argument, and an argument is still a value.
    ///
    /// <para>
    /// This is the control against the rule becoming a suppression: if "the value starts with a member call"
    /// were answered by skipping the whole capture, this credential would be lost, and it is the same line
    /// and the same key as the false positive that motivated the rule.
    /// </para>
    /// </summary>
    [Fact]
    public void ACredentialWrittenAsAnArgumentToARecognisedCall_IsStillReported()
    {
        var token = TestData.ValueShapedToken(24);
        var line = "        var auth = WorkbookCompatibilityReader.AuthorizeWrite(password:" + token + ");";

        var findings = _scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", line);

        var finding = Assert.Single(findings);
        Assert.Equal("credential-shaped-value", finding.RuleName);
    }

    /// <summary>
    /// <b>The argument a call is carrying, when the argument is QUOTED.</b> The pattern cannot see it: its
    /// bare-value branch stops at the first quote, so the capture is <c>Reader.AuthorizeWrite(</c> and the
    /// literal after the bracket is outside the match entirely. The value is therefore read from the rest of
    /// the statement and judged as a value.
    ///
    /// <para>
    /// <b>This case was lost by both former implementations</b>, and it is the reason the unified rule reads a
    /// reference LENGTH rather than answering a yes-or-no question about the capture. Measured on the
    /// pre-W9.4 scanner: this line was reported — but for the wrong reason, on the code text, and the same
    /// finding appeared when the literal was <c>read</c> instead of a credential. Measured on the
    /// brace-skipping fix: not reported at all. A rule whose justification is "the arguments are still judged"
    /// has to keep that true when the argument is quoted.
    /// </para>
    /// </summary>
    [Fact]
    public void AQuotedArgumentToARecognisedCall_IsStillReported()
    {
        var token = DenseSegment(32);
        var line = "        var auth = WorkbookCompatibilityReader.AuthorizeWrite(\"" + token + "\");";

        var findings = _scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", line);

        var finding = Assert.Single(findings);
        Assert.Equal("credential-shaped-value", finding.RuleName);
        Assert.Equal(token.Length, finding.ValueLength);
    }

    /// <summary>
    /// A named argument the pattern CANNOT reach: written <c>password:"…"</c>, the bare-value branch swallows
    /// the argument's own key into the capture and stops at the space, so the pattern has no second key to
    /// fire on and the literal is beyond its match. It is read from the statement instead — <b>once</b>, under
    /// the assignment's key, because that key is what put the statement in scope.
    /// </summary>
    [Fact]
    public void ANamedArgumentThePatternCannotReach_IsReportedOnce()
    {
        var token = DenseSegment(32);
        var line = "        var auth = WorkbookCompatibilityReader.AuthorizeWrite(password:\"" + token + "\");";

        var finding = Assert.Single(_scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", line));

        Assert.Equal("credential-shaped-value", finding.RuleName);
        Assert.Equal("auth", finding.KeyName);
        Assert.Equal(token.Length, finding.ValueLength);
    }

    /// <summary>
    /// And the other spelling of the same statement, one space later: <c>password: "…"</c> ends the pattern's
    /// bare-value capture at the bracket, so the pattern reaches the argument itself on its own next match and
    /// reports it under the argument's key. The argument reader must then stand aside: without that, one
    /// literal would be reported twice — once under <c>auth</c> and once under <c>password</c> — and a doubled
    /// finding is how a gate's output stops being read.
    /// </summary>
    [Fact]
    public void ANamedArgumentThePatternReaches_IsReportedOnce_UnderItsOwnKey()
    {
        var token = DenseSegment(32);
        var line = "        var auth = WorkbookCompatibilityReader.AuthorizeWrite( password: \"" + token + "\");";

        var finding = Assert.Single(_scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", line));

        Assert.Equal("credential-shaped-value", finding.RuleName);
        Assert.Equal("password", finding.KeyName);
    }

    /// <summary>
    /// The control in the other direction: reading a call's quoted arguments must not turn an argument into a
    /// finding. Both literals here are ordinary code text — an environment-variable identifier and a prose
    /// value — and the same judgement that keeps them out of a config file keeps them out of an argument list.
    /// </summary>
    [Fact]
    public void HarmlessQuotedArgumentsToARecognisedCall_AreNotReported()
    {
        const string line =
            """        var auth = Client.Authenticate("NEXUS_DEV_READONLY", "read-only");""";

        Assert.Empty(_scanner.ScanText("src/Nexus.ProductCore.Core/Startup.cs", line));
    }

    /// <summary>
    /// An unterminated quote ends the reading: there is no literal whose end is known, and guessing where one
    /// would have been would be inventing text to judge. The scanner declines, which is the same choice it
    /// makes everywhere else it cannot see the whole of something.
    /// </summary>
    [Fact]
    public void AnUnterminatedQuoteAfterARecognisedCall_IsNotGuessedAt()
    {
        const string line = """        var auth = Client.Authenticate("unterminated""";

        Assert.Empty(_scanner.ScanText("src/Nexus.ProductCore.Core/Startup.cs", line));
    }

    /// <summary>
    /// Recognising one executable expression must not suppress <b>the rest of its line</b>. The first
    /// statement is the false positive; the second assignment on the same line is a credential. A rule that
    /// skipped the match, or skipped the line, would lose it.
    /// </summary>
    [Fact]
    public void ARecognisedCall_DoesNotHideASecondAssignmentOnTheSameLine()
    {
        var token = DenseSegment(32);
        var line = "        var auth = WorkbookCompatibilityReader.AuthorizeWrite(read); var apiKey = \""
            + token + "\";";

        var finding = Assert.Single(_scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", line));

        Assert.Equal("apiKey", finding.KeyName);
    }

    /// <summary>The same control across a line boundary: the recognised call must not blind the scan to
    /// what follows it.</summary>
    [Fact]
    public void ARecognisedCall_DoesNotHideAnAssignmentOnTheFollowingLine()
    {
        var token = DenseSegment(32);
        var text = "        var auth = WorkbookCompatibilityReader.AuthorizeWrite(read, form);\n"
            + "        var apiKey = \"" + token + "\";";

        var finding = Assert.Single(_scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", text));

        Assert.Equal("apiKey", finding.KeyName);
    }

    /// <summary>
    /// <b>The discriminator.</b> The identical characters as the line above, quoted. Quoting a member call
    /// takes it out of the language: there is no expression to execute, so there is no code reference to
    /// recognise, and the text is judged as the value it now is. This is the requirement that forces the
    /// capture position to be an input to the judgement — no property of the string can tell the two apart.
    ///
    /// <para>
    /// The fixture is <b>derived from the verbatim line-139 literal</b> rather than written out again, so the
    /// two tests provably carry the same text and cannot drift apart. It is built at run time because the
    /// quoted form of line 139 IS a credential-shaped literal by the scanner's own rules — that is the whole
    /// finding — and this file is scanned by this scanner. A committed copy would (correctly) fail the gate
    /// on the test that documents it.
    /// </para>
    ///
    /// <para>
    /// <b>Stated plainly, because it is a real cost:</b> a quoted string holding a call-shaped expression is
    /// reported, and in real documentation that could be a false positive. It is accepted deliberately.
    /// Quoting two characters is unbounded reach for anyone who wants an escape from this gate, whereas the
    /// false positive needs an unquoted assignment-to-a-credential-named key whose value is quoted and holds
    /// a member call with a ≥20-character unbroken run. The alternative — exempting quoted text by shape —
    /// would be an exemption anyone could acquire with two keystrokes.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSameTextQuoted_IsStillJudgedAsAValue()
    {
        const string unquoted = """        var auth = WorkbookCompatibilityReader.AuthorizeWrite(""";
        var quoted = unquoted.Replace("auth = ", "auth = \"", StringComparison.Ordinal) + "\"";

        var findings = _scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", quoted);

        var finding = Assert.Single(findings);
        Assert.Equal("credential-shaped-value", finding.RuleName);
        Assert.Equal("auth", finding.KeyName);
    }

    /// <summary>
    /// A fully-qualified type name — the other half of the class, and a NAME rather than a value: it is a
    /// chain of name-shaped segments with no call syntax. It carries an unbroken run of 44 characters, far
    /// past the threshold, and is still not a credential.
    ///
    /// <para>
    /// The name shape is position-independent, like the bare-identifier rule beside it and for the same
    /// reason: a name is a name whether or not it is quoted. What the position governs is narrower — whether
    /// text may be read as <i>executable syntax</i> — so <c>A.B(</c> quoted is a value while <c>A.B</c> is a
    /// name either way.
    /// </para>
    /// </summary>
    [Fact]
    public void AQualifiedTypeName_IsNotReported()
    {
        const string line = """        apiKey: "Nexus.ProductCore.Core.DevelopmentControl.DevelopmentControlWriterAuthorizer",""";

        Assert.Empty(_scanner.ScanText("src/Nexus.ProductCore.Core/Startup.cs", line));

        const string name = "Nexus.ProductCore.Core.DevelopmentControl.DevelopmentControlWriterAuthorizer";
        Assert.True(CredentialShape.IsQualifiedNameShape(name));
        Assert.True(CredentialShape.IsCodeReferenceShape(name));
    }

    /// <summary>
    /// <b>The near miss that matters, and the one the competing implementation lost.</b> A JSON web token is
    /// DOTTED, and its segments are long unbroken runs — so a fix that exempted "anything with dots" would
    /// stop catching it. The exemption is therefore per-segment: a segment that is not itself a name is a
    /// generated token, and the text is a credential that merely contains dots. That is why the parse tests
    /// each segment with <see cref="CredentialShape.IsBareIdentifierShape"/> rather than accepting any
    /// identifier-shaped run.
    ///
    /// <para>
    /// Each segment is built to be dense <b>by construction</b> — mixed case AND a digit are forced in —
    /// rather than drawn at random and hoped to be. D-4, recorded in this estate, is what a
    /// probabilistically-dense fixture costs: a gate that passes on a coin toss.
    /// </para>
    /// </summary>
    [Fact]
    public void ADottedTokenWhoseSegmentsAreDense_IsStillReported()
    {
        var token = DenseSegment(24) + "." + DenseSegment(24) + "." + DenseSegment(24);

        var findings = _scanner.ScanText("appsettings.json", "token = \"" + token + "\"");

        var finding = Assert.Single(findings);
        Assert.Equal("credential-shaped-value", finding.RuleName);
        Assert.Equal("token", finding.KeyName);

        // And the same text WITHOUT quotes, which is the position the reference rule is reachable from. If
        // the rule read only the capture position and not the segment shapes, this is where an unquoted
        // token would disappear.
        var bare = _scanner.ScanText("appsettings.json", "token = " + token);
        Assert.Single(bare);
    }

    /// <summary>
    /// The second near miss, on the other clause: a dotted token whose segments are not identifiers at all,
    /// because base64url's <c>-</c> and <c>_</c> appear inside them. Dotted, machine-generated, no segment
    /// name-shaped — reported, quoted and unquoted.
    /// </summary>
    [Fact]
    public void ADottedTokenWithNonIdentifierSegments_IsStillReported()
    {
        var token = TestData.ValueShapedToken(24) + "-" + TestData.ValueShapedToken(24)
            + "." + TestData.ValueShapedToken(24) + "_" + TestData.ValueShapedToken(24);

        Assert.Single(_scanner.ScanText("appsettings.json", "token = \"" + token + "\""));
        Assert.Single(_scanner.ScanText("appsettings.json", "token = " + token));
    }

    /// <summary>
    /// The tightest near miss available: the SAME key, the same statement, and a value where line 139 has an
    /// expression. If the new rule were in effect a suppression of <c>auth =</c> rather than a test of shape,
    /// this is the test that fails.
    /// </summary>
    [Fact]
    public void TheSameKeyCarryingAValueInsteadOfAnExpression_IsStillReported()
    {
        var token = DenseSegment(32);
        var line = "        var auth = \"" + token + "\";";

        var findings = _scanner.ScanText(
            "src/Nexus.ProductCore.Core/DevelopmentControl/DevelopmentControlContractAdapters.cs", line);

        var finding = Assert.Single(findings);
        Assert.Equal("credential-shaped-value", finding.RuleName);
        Assert.Equal("auth", finding.KeyName);
    }

    /// <summary>
    /// Non-vacuity for the predicate itself: it must be false for everything that is not a code reference,
    /// or the controls above would pass while the rule exempted anything at all.
    /// </summary>
    [Theory]
    [InlineData("Xk9Qm2Vt7Lp4Rb8Nzt3Hy6Wb1")]   // no dot: no chain structure to call a reference
    [InlineData("A1bC2dE3fG4hI5jK6lM7nO8p")]     // one dense token, no separators
    [InlineData("Abc1.Def2")]                     // dotted, but a segment is a dense token, not a name
    [InlineData("Abc.def-ghi")]                   // dotted, but a segment is not identifier-shaped
    [InlineData("Abc.Def ghi")]                   // whitespace: a reference is a single token, this is prose
    [InlineData(".Def")]                          // an empty segment is not a name
    [InlineData("Abc.")]                          // likewise
    public void ValuesThatAreNotCodeReferences_AreNotExempted(string value)
    {
        Assert.False(CredentialShape.IsCodeReferenceShape(value), $"'{value}' should NOT be a code reference.");
        Assert.False(CredentialShape.IsQualifiedNameShape(value), $"'{value}' should NOT be a qualified name.");
    }

    /// <summary>
    /// The other direction of the same pair. "Wholly a reference" is the strict form — the text is a chain
    /// and, if a bracket follows it, that bracket is the whole argument list. A call that <i>carries</i>
    /// arguments is deliberately <b>not</b> listed here: it is a reference followed by text that still has
    /// to be judged, which is the property that keeps requirement 4 true and is asserted in
    /// <see cref="TheReferenceParse_ConsumesTheCallSyntaxAndNothingMore"/>.
    /// </summary>
    [Theory]
    [InlineData("WorkbookCompatibilityReader.AuthorizeWrite")]
    [InlineData("WorkbookCompatibilityReader.AuthorizeWrite(")]
    [InlineData("WorkbookCompatibilityReader.AuthorizeWrite()")]
    [InlineData("configuration.GetConnectionString(")]
    [InlineData("configuration.GetConnectionString()")]
    [InlineData("FormMap.ToImplementation()")]
    [InlineData("_logger.LogInformation(")]
    [InlineData("Nexus.ProductCore.Core.DevelopmentControl.DevelopmentControlWriterAuthorizer")]
    public void CodeReferences_AreRecognisedAsCode(string value)
        => Assert.True(CredentialShape.IsCodeReferenceShape(value), $"'{value}' should be recognised as a code reference.");

    /// <summary>
    /// The qualified-name predicate, separated from the code-reference predicate so the difference between
    /// them is a test rather than a comment: a name has no call syntax, and text that has call syntax is not
    /// a name. The two are derived from one parse, which is what makes this the only difference.
    /// </summary>
    [Theory]
    [InlineData("Nexus.ProductCore.Core.DevelopmentControl.DevelopmentControlWriterAuthorizer", true)]
    [InlineData("WorkbookCompatibilityReader.AuthorizeWrite", true)]
    [InlineData("_logger.LogInformation", true)]
    [InlineData("WorkbookCompatibilityReader.AuthorizeWrite(", false)]
    [InlineData("WorkbookCompatibilityReader.AuthorizeWrite()", false)]
    [InlineData("FormMap.ToImplementation(intendedForm)", false)]
    [InlineData("configuration.GetConnectionString(", false)]
    public void AQualifiedName_IsAChainWithoutCallSyntax(string value, bool isName)
        => Assert.Equal(isName, CredentialShape.IsQualifiedNameShape(value));

    /// <summary>
    /// The one parse, exercised where its two answers must differ: the reference ends at the bracket, the
    /// chain ends before it, and an argument list is not consumed as part of either. A rule that consumed too
    /// much would exempt an argument; one that consumed too little would leave the call site to be judged as
    /// a credential.
    /// </summary>
    [Fact]
    public void TheReferenceParse_ConsumesTheCallSyntaxAndNothingMore()
    {
        Assert.True(CredentialShape.TryReadCodeReference("WorkbookCompatibilityReader.AuthorizeWrite(read,form)", out var open));
        Assert.Equal("WorkbookCompatibilityReader.AuthorizeWrite(".Length, open);

        Assert.True(CredentialShape.TryReadCodeReference("WorkbookCompatibilityReader.AuthorizeWrite()", out var empty));
        Assert.Equal("WorkbookCompatibilityReader.AuthorizeWrite()".Length, empty);

        Assert.True(CredentialShape.TryReadCodeReference("WorkbookCompatibilityReader.AuthorizeWrite", out var bare));
        Assert.Equal("WorkbookCompatibilityReader.AuthorizeWrite".Length, bare);

        Assert.False(CredentialShape.TryReadCodeReference("Xk9Qm2Vt7Lp4Rb8Nzt3Hy6Wb1", out var none));
        Assert.Equal(0, none);

        // A call that carries arguments is a reference FOLLOWED BY something, and the whole-text predicate
        // says so: this is the property that stops the arguments being exempted with the reference.
        Assert.False(CredentialShape.IsCodeReferenceShape("FormMap.ToImplementation(intendedForm)"));
        Assert.False(CredentialShape.IsCodeReferenceShape("WorkbookCompatibilityReader.AuthorizeWrite(read,form)"));
    }

    /// <summary>
    /// A generated segment that is dense <b>by construction</b>: mixed case and a digit are forced in at
    /// fixed positions, so the fixture cannot pass or fail by luck, and the value is never a literal this
    /// repository has committed. See <see cref="TestData.ValueShapedToken"/> for the measured flake this
    /// replaces.
    /// </summary>
    private static string DenseSegment(int length)
        => "A" + new string('q', length - 3) + "z9";
}
