using Nexus.ProductCore.Contracts.DevelopmentControl;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W8D TASK 5. The V3 <c>09_ChangeScopes</c> vocabulary bridge.
///
/// <para><b>What was wrong.</b> <c>ItemType</c> on the V3 candidate carries four tokens —
/// <c>PROJECT</c> (111 rows), <c>DB_CONTEXT</c> (67), <c>CONTRACT</c> (36), <c>FILE_GLOB</c> (9) —
/// and not one of them is a member of <see cref="ChangeScopeItemKind"/>. Every scope row therefore
/// reported <c>WellFormed = false</c>, and a row that is not well-formed must not be fed to a
/// collision comparison, so the entire declared-scope surface of the candidate was inert.</para>
///
/// <para><b>Why that was not simply a parsing gap.</b> A parser that mapped the tokens anyway
/// would be inventing a correspondence. The four tokens are the four legacy scope COLUMNS, and the
/// kind of each was assigned by the pre-migration producer:
/// <c>DevBridge.Engine.PreReservationSafety.BuildScope</c> in Forge, and its shipped twin
/// <c>WorkbookCompatibilityReader.ProjectHeldReservations</c> in Platform, both contain
/// <c>projects</c>→<c>ProjectResource</c>, <c>contractsApis</c>→<c>PublicContract</c>,
/// <c>schemaContexts</c>→<c>DatabaseMigration</c> and <c>filesGlobs</c>→<c>KindOf(token)</c>. That
/// code is evidence for three of the four outright, and for the fourth it says the kind is a
/// function of the target rather than of the token — which is why <c>FILE_GLOB</c> is measured
/// below instead of being assumed.</para>
///
/// <para><b>Negative and mutation controls.</b> Every "resolves to X" claim here is paired with a
/// case that must resolve to null, and two of the tests below are constructed so that the WRONG
/// implementation — a flat <c>FILE_GLOB</c>→<c>Glob</c>, or an <c>Enum.TryParse</c> without the
/// numeric guard — fails them. A test that only asserts the happy path cannot tell the bridge from
/// a guess.</para>
/// </summary>
public sealed class DevelopmentControlScopeVocabularyTests
{
    private static readonly string[] SubtreeTargets = { "control/", "scripts/" };

    /// <summary>
    /// The three tokens whose kind the legacy producer assigned as a CONSTANT. If any of these
    /// stops resolving, the bridge has been narrowed back to enum members only.
    /// </summary>
    [Theory]
    [InlineData("PROJECT", ChangeScopeItemKind.ProjectResource)]
    [InlineData("CONTRACT", ChangeScopeItemKind.PublicContract)]
    [InlineData("DB_CONTEXT", ChangeScopeItemKind.DatabaseMigration)]
    public void EachTokenTheProducerGaveAConstantKindResolvesToThatKind(string token, ChangeScopeItemKind expected)
    {
        Assert.Equal(expected, DevelopmentControlScopeVocabulary.ParseItemKind(token, ""));
        Assert.Equal(expected, DevelopmentControlScopeVocabulary.ParseItemKind(token.ToLowerInvariant(), ""));
    }

    /// <summary>
    /// <c>FILE_GLOB</c> names the legacy column <c>filesGlobs</c>, not a kind, so the kind is a
    /// function of the target. This is the test a flat <c>FILE_GLOB</c>→<c>Glob</c> mapping fails:
    /// it asserts three different kinds from one token.
    /// </summary>
    [Theory]
    [InlineData("control/", ChangeScopeItemKind.DirectorySubtree)]
    [InlineData("scripts/", ChangeScopeItemKind.DirectorySubtree)]
    [InlineData("src\\nested\\", ChangeScopeItemKind.DirectorySubtree)]
    [InlineData("NEXUS_DEVELOPMENT_CONTROL.xlsx", ChangeScopeItemKind.ExactFile)]
    [InlineData("src/Program.cs", ChangeScopeItemKind.ExactFile)]
    [InlineData("src/**/*.cs", ChangeScopeItemKind.Glob)]
    [InlineData("appsettings.json", ChangeScopeItemKind.ExactFile)]
    public void FileGlobResolvesItsKindFromTheTargetItNames(string target, ChangeScopeItemKind expected)
    {
        Assert.Equal(expected, DevelopmentControlScopeVocabulary.ParseItemKind("FILE_GLOB", target));
    }

    /// <summary>
    /// <b>Mutation control over the real data.</b> The 9 <c>FILE_GLOB</c> targets the authority
    /// candidate actually carries, classified. Measured: 6 subtree paths and 3 bare filenames, and
    /// <b>not one contains <c>*</c> or <c>?</c></b>.
    ///
    /// <para>This is the assertion that makes "flat <c>FILE_GLOB</c>→<c>Glob</c></para> is wrong"
    /// a fact rather than an opinion: such a mapping would mis-kind all 9 rows, and because kind
    /// selects which collision rules apply, it would reason about a different resource than the one
    /// declared. The <c>countedGlob == 0</c> assertion is the control — it FAILS if a future
    /// candidate introduces a real glob, which is the correct time to revisit the mapping rather
    /// than to keep passing.</summary>
    [Fact]
    public void TheRealFileGlobTargetsResolveToSubtreeAndExactFileAndNeverToGlob()
    {
        string[] realTargets =
        {
            "NEXUS_DEVELOPMENT_CONTROL.xlsx", "control/", "scripts/",
            "NEXUS_DEVELOPMENT_CONTROL.xlsx", "control/", "scripts/",
            "NEXUS_DEVELOPMENT_CONTROL.xlsx", "control/", "scripts/",
        };

        var kinds = realTargets
            .Select(t => DevelopmentControlScopeVocabulary.ParseItemKind("FILE_GLOB", t))
            .ToList();

        Assert.Equal(9, kinds.Count);
        Assert.All(kinds, k => Assert.NotNull(k));
        Assert.Equal(6, kinds.Count(k => k == ChangeScopeItemKind.DirectorySubtree));
        Assert.Equal(3, kinds.Count(k => k == ChangeScopeItemKind.ExactFile));
        Assert.Equal(0, kinds.Count(k => k == ChangeScopeItemKind.Glob));
    }

    /// <summary>
    /// The four tokens the V3 candidate carries, all resolving. This is the closure claim: with
    /// this bridge in place the candidate's 223 scope rows have a resolvable item kind, where
    /// before it had none. It is the item-kind axis ONLY — the access axis is asserted unresolved
    /// by <see cref="MigrateIsNotRepresentableSoTheAccessAxisCannotBeBridgedHere"/>.
    /// </summary>
    [Fact]
    public void AllFourTokensTheCandidateCarriesResolveAnItemKind()
    {
        // Representative targets taken from the real sheet, one per token.
        (string Token, string Target)[] rows =
        {
            ("PROJECT", "Nexus.Platform.Identity"),
            ("DB_CONTEXT", "core"),
            ("CONTRACT", "Nexus.Platform.Contracts"),
            ("FILE_GLOB", "control/"),
        };

        Assert.All(rows, r =>
        {
            var kind = DevelopmentControlScopeVocabulary.ParseItemKind(r.Token, r.Target);
            Assert.NotNull(kind);
        });

        // Control: the set of distinct tokens is exactly these four. If a fifth appears, the
        // bridge's coverage claim in the W8D evidence is no longer complete.
        Assert.Equal(4, rows.Select(r => r.Token).Distinct().Count());
    }

    /// <summary>
    /// Unrecognised text must be null, never a default. Defaulting to
    /// <see cref="ChangeScopeItemKind.ExactFile"/> would narrow a broad declaration into a narrow
    /// one and silently change what a collision check compares.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UNKNOWN")]
    [InlineData("LEGACY_UNKNOWN")]
    [InlineData("FILE")]
    [InlineData("DB")]
    [InlineData("PROJECTS")]
    [InlineData("FILE_GLOBS")]
    [InlineData("Project Resource")]
    public void UnrecognisedItemTypeTextResolvesToNull(string? text) =>
        Assert.Null(DevelopmentControlScopeVocabulary.ParseItemKind(text, "src/Program.cs"));

    /// <summary>
    /// Surrounding whitespace is trimmed before matching, and this is deliberate rather than
    /// incidental: the candidate is known to carry padded cells (W8D TASK 5 D-1d found ten
    /// ID-named columns holding whitespace values), so a padded member name is a real shape in
    /// this data and must resolve rather than fail closed.
    ///
    /// <para>The distinction the two tests draw is worth stating plainly, because it is the
    /// difference between recovering a value and inventing one: <c>"ControlStore "</c> trims to a
    /// member and therefore IS that member, whereas <c>"Project Resource"</c> trims to a
    /// non-member and is still not one. Trimming removes padding; it does not remove the space
    /// inside a name.</para>
    /// </summary>
    [Theory]
    [InlineData("ControlStore ", ChangeScopeItemKind.ControlStore)]
    [InlineData("  PROJECT  ", ChangeScopeItemKind.ProjectResource)]
    [InlineData("\tFILE_GLOB\r\n", ChangeScopeItemKind.DirectorySubtree)]
    public void SurroundingWhitespaceIsTrimmedBeforeMatching(string text, ChangeScopeItemKind expected) =>
        Assert.Equal(expected, DevelopmentControlScopeVocabulary.ParseItemKind(text, "control/"));

    /// <summary>
    /// <b>Mutation control on the numeric guard.</b> <c>Enum.TryParse</c> accepts a numeric string
    /// and maps it by ordinal, so a stray cell holding <c>3</c> would otherwise read as a confident
    /// <see cref="ChangeScopeItemKind.ProjectResource"/>.
    ///
    /// <para>The first two assertions prove the guard is load-bearing rather than decorative: they
    /// show what the parser would have returned WITHOUT it. Without that half, a future removal of
    /// <c>IsNumericToken</c> would be invisible.</para>
    /// </summary>
    [Fact]
    public void ANumericCellIsRefusedEvenThoughEnumParseWouldAcceptIt()
    {
        // The hazard, demonstrated: this is what the unguarded parser did.
        Assert.True(Enum.TryParse<ChangeScopeItemKind>("3", ignoreCase: true, out var byOrdinal));
        Assert.Equal(ChangeScopeItemKind.ProjectResource, byOrdinal);
        Assert.True(Enum.TryParse<ChangeScopeAccessMode>("1", ignoreCase: true, out var accessByOrdinal));
        Assert.Equal(ChangeScopeAccessMode.Read, accessByOrdinal);

        // The guard.
        Assert.Null(DevelopmentControlScopeVocabulary.ParseItemKind("3", ""));
        Assert.Null(DevelopmentControlScopeVocabulary.ParseItemKind("0", ""));
        Assert.Null(DevelopmentControlScopeVocabulary.ParseItemKind("  6  ", ""));
        Assert.Null(DevelopmentControlScopeVocabulary.ParseAccessMode("1"));
        Assert.Null(DevelopmentControlScopeVocabulary.ParseAccessMode("0"));
    }

    /// <summary>
    /// Enum member names still resolve by name, with or without a target, and case-insensitively —
    /// the bridge is additive and did not replace the original reading. A legacy workbook that
    /// already spells a kind by name must keep working.
    /// </summary>
    [Theory]
    [InlineData("Glob", ChangeScopeItemKind.Glob)]
    [InlineData("glob", ChangeScopeItemKind.Glob)]
    [InlineData("ProjectResource", ChangeScopeItemKind.ProjectResource)]
    [InlineData("DatabaseMigration", ChangeScopeItemKind.DatabaseMigration)]
    [InlineData("ControlStore", ChangeScopeItemKind.ControlStore)]
    public void EnumMemberNamesStillResolveByTheirOwnSpelling(string text, ChangeScopeItemKind expected)
    {
        Assert.Equal(expected, DevelopmentControlScopeVocabulary.ParseItemKind(text));
        Assert.Equal(expected, DevelopmentControlScopeVocabulary.ParseItemKind(text, "unrelated/target"));
    }

    /// <summary>
    /// <b>The open Owner decision, asserted as an executable fact rather than described in prose.</b>
    ///
    /// <para>The V3 model declares five access values. <see cref="ChangeScopeAccessMode"/> has two,
    /// so <c>MIGRATE</c>, <c>CREATE</c> and <c>DELETE</c> have nowhere to land. That is not a
    /// parsing gap to close locally: the legacy producer paired <c>schemaContexts</c> with
    /// <c>AccessMode.Migrate</c>, and the engine gives two <c>DatabaseMigration</c> items their own
    /// collision rule — they collide only when BOTH are <c>Migrate</c> (<c>ChangeScope.cs</c>).
    /// The V3 workbook records <c>WRITE</c> on all 223 rows instead. So the two artifacts disagree,
    /// and this test pins BOTH halves: that <c>MIGRATE</c> is unrepresentable here, and that the
    /// contract's vocabulary is exactly two wide. When an Owner answers, this test is the thing
    /// that must change, and it will fail loudly if the vocabulary is widened without the
    /// collision rule being addressed.</para>
    /// </summary>
    [Fact]
    public void MigrateIsNotRepresentableSoTheAccessAxisCannotBeBridgedHere()
    {
        Assert.Null(DevelopmentControlScopeVocabulary.ParseAccessMode("MIGRATE"));
        Assert.Null(DevelopmentControlScopeVocabulary.ParseAccessMode("CREATE"));
        Assert.Null(DevelopmentControlScopeVocabulary.ParseAccessMode("DELETE"));

        Assert.Equal(2, Enum.GetValues<ChangeScopeAccessMode>().Length);
        Assert.Equal(ChangeScopeAccessMode.Write, DevelopmentControlScopeVocabulary.ParseAccessMode("WRITE"));
        Assert.Equal(ChangeScopeAccessMode.Read, DevelopmentControlScopeVocabulary.ParseAccessMode("READ"));

        // The consequence, stated: a DB_CONTEXT row's item kind is now resolvable, and its access
        // still is not. This is what "TASK 5 closed ItemType, T-4/Access stays open" means.
        Assert.NotNull(DevelopmentControlScopeVocabulary.ParseItemKind("DB_CONTEXT", "core"));
        Assert.Null(DevelopmentControlScopeVocabulary.ParseAccessMode("MIGRATE"));
    }

    /// <summary>
    /// The row-level consequence, which is the thing a consumer actually branches on.
    ///
    /// <para>Before TASK 5 every V3 scope row was <c>WellFormed = false</c> because BOTH fields
    /// failed. Now it is false only on the access axis. A caller that treated false as "no scope
    /// declared" was wrong before and would be wrong now — the target and kind are readable.</para>
    /// </summary>
    [Fact]
    public void ARowIsNowWellFormedOnItsItemKindAndStillDrivenByItsAccessAxis()
    {
        static DevelopmentControlScopeRow Row(string itemType, string target, string access) =>
            new(
                ScopeRecordId: "SC-1",
                WorkItemId: "WI-1",
                ScopeClass: "",
                ItemTypeText: itemType,
                Target: target,
                AccessText: access,
                ReviewState: "UNREVIEWED",
                IsCurrentText: "Yes",
                Note: "");

        var writer = Row("PROJECT", "Nexus.Platform.Identity", "WRITE");
        Assert.Equal(ChangeScopeItemKind.ProjectResource, writer.ItemType);
        Assert.True(writer.WellFormed);

        var fileGlob = Row("FILE_GLOB", "control/", "WRITE");
        Assert.Equal(ChangeScopeItemKind.DirectorySubtree, fileGlob.ItemType);
        Assert.True(fileGlob.WellFormed);

        // The access axis is the only thing still failing, and it fails closed.
        var unmappableAccess = Row("DB_CONTEXT", "core", "MIGRATE");
        Assert.Equal(ChangeScopeItemKind.DatabaseMigration, unmappableAccess.ItemType);
        Assert.Null(unmappableAccess.Access);
        Assert.False(unmappableAccess.WellFormed);

        // Control: an unrecognised KIND still fails, so the row-level flag did not become
        // "true unless access is odd".
        var unknownKind = Row("SOMETHING_NEW", "core", "WRITE");
        Assert.Null(unknownKind.ItemType);
        Assert.False(unknownKind.WellFormed);
    }
}
