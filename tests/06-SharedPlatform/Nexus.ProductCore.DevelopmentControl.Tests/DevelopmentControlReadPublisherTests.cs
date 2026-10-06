using System.Text.Json;
using Nexus.ProductCore.Contracts.ReadModel;
using Nexus.ProductCore.Core.ReadModel;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.4 TASK 23 — the DevelopmentControl read-model publisher and the atomicity of its publication.
///
/// <para>
/// Each property is asserted with a control that would fail if it were merely assumed. The refusals
/// matter more than the happy path: a publisher that reports "published" for an authority it could not
/// reach is worse than one that throws, because the resulting document is VALID and reports an empty
/// control rather than an unreachable one.
/// </para>
///
/// <para>
/// <b>Fixtures are real workbooks.</b> <see cref="WorkbookFixtureBuilder"/> writes OOXML derived from
/// the same compatibility map the production reader uses, so a projection written against the wrong
/// sheet or column names fails here rather than in production — which is exactly the defect this
/// milestone measured once already, where reading <c>SchemaId</c> as a column produced a control whose
/// identity was blank while every other field looked correct.
/// </para>
///
/// <para>
/// <b>LANE: this class is deliberately UNTAGGED, so the portable lane runs it.</b> The append suites
/// beside it are <c>WindowsOnly</c> because they acquire a reservation through <c>AtomicWriterLock</c>,
/// whose path normaliser is Windows-shaped by construction. This publisher takes NO lock and writes
/// NOTHING to the authority — it reads through <c>WorkbookCompatibilityReader.Read</c> and writes only
/// to the destination it was given. Tagging it Windows-only would exclude from Linux CI the one suite
/// that proves the publishing boundary, for a constraint it does not share. If it fails on Linux, that
/// is a finding about the reader's path handling and must be reported, not silenced by a tag.
/// </para>
/// </summary>
public sealed class DevelopmentControlReadPublisherTests : IDisposable
{
    private const string ObservedAt = "2026-10-05T00:00:00Z";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "w104-publish-" + Guid.NewGuid().ToString("N")[..12]);

    public DevelopmentControlReadPublisherTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // An inert temp directory is not a test failure.
        }
    }

    // The authority sits in its OWN directory. Counting siblings in a directory that also contains the
    // destination would have measured 'the publisher created a directory' rather than 'the publisher
    // wrote beside the workbook', and the first version of this test did exactly that.
    private string AuthorityDir => Path.Combine(_root, "authority");
    private string WorkbookPath => Path.Combine(AuthorityDir, "NEXUS_DEVELOPMENT_CONTROL.xlsx");
    private string Destination => Path.Combine(_root, "published");
    private string PublishedPath => Path.Combine(Destination, DevelopmentControlReadContract.FileName);

    private DevelopmentControlReadPublisher Publisher() =>
        new(new DevelopmentControlReadProjection(WorkbookPath));

    /// <summary>A real V3 workbook whose four authority sites read AUTHORITATIVE.</summary>
    private void WriteAuthoritativeWorkbook()
    {
        Directory.CreateDirectory(AuthorityDir);
        WorkbookFixtureBuilder.Authoritative(WorkbookPath);
    }

    // ================================================================ identity

    [Fact]
    public void Publishes_and_names_its_contract_authority_and_instant()
    {
        WriteAuthoritativeWorkbook();

        var outcome = Publisher().Publish(Destination, ObservedAt);

        Assert.True(outcome.Published, outcome.Reason);
        Assert.True(File.Exists(PublishedPath));

        using var document = JsonDocument.Parse(File.ReadAllText(PublishedPath));
        var source = document.RootElement.GetProperty("Source");

        Assert.Equal(DevelopmentControlReadContract.SchemaVersion, document.RootElement.GetProperty("SchemaVersion").GetString());
        Assert.Equal(DevelopmentControlReadContract.Authority, source.GetProperty("Authority").GetString());
        Assert.Equal(ObservedAt, source.GetProperty("ObservedAt").GetString());
        Assert.Equal(DevelopmentControlReadContract.FileName, Path.GetFileName(PublishedPath));
    }

    [Fact]
    public void The_published_control_has_a_NON_BLANK_schema_identity()
    {
        // The measured defect this guards. `00_Control` binds two logical columns, `ControlItem` and
        // `Value`; SchemaId and SchemaVersion are ROWS in that sheet. Read as COLUMNS they return the
        // empty string, and the result is a document whose control identity is blank while every other
        // field is correct — the kind of wrong that passes a casual review.
        WriteAuthoritativeWorkbook();
        Assert.True(Publisher().Publish(Destination, ObservedAt).Published);

        var model = ReadPublished();

        Assert.False(string.IsNullOrWhiteSpace(model.Payload.Control.SchemaId),
            "the control's schema identity must be read from its own rows, not as columns");
        Assert.False(string.IsNullOrWhiteSpace(model.Payload.Control.SchemaVersion));
        Assert.True(model.Payload.Control.SheetCount > 0, "the bound sheet count must be measured, not defaulted");
        Assert.Equal("AUTHORITATIVE", model.Payload.Control.State);
    }

    [Fact]
    public void The_projection_binds_the_workbooks_sheets_rather_than_publishing_a_blank_document()
    {
        // Non-vacuity, at the level this fixture can actually support. `WorkbookFixtureBuilder` writes
        // the headers and control rows derived from the compatibility map but NO data rows, so an
        // empty WorkItems list here is CORRECT rather than a defect — the first version of this test
        // asserted NotEmpty and failed for that reason.
        //
        // What must hold regardless is that the projection RESOLVED the physical sheets. A projection
        // whose sheet lookups all missed would produce a structurally perfect document reporting an
        // empty control, and every other assertion in this file would still pass.
        WriteAuthoritativeWorkbook();
        var outcome = Publisher().Publish(Destination, ObservedAt);
        Assert.True(outcome.Published, outcome.Reason);

        var payload = ReadPublished().Payload;

        Assert.True(payload.Control.SheetCount > 0,
            "a projection that bound no sheets would publish a control claiming zero bound sheets");
        Assert.Equal("AUTHORITATIVE", payload.Control.State);
        Assert.False(string.IsNullOrWhiteSpace(payload.Control.SchemaId));
        Assert.Empty(payload.WorkItems); // asserted, so a fixture that gains rows makes this test fail loudly
    }

    [Fact]
    public void The_projection_publishes_the_records_a_real_authority_holds()
    {
        // The non-vacuity check the fixture cannot give: read the LIVE authority, if this host has one.
        // Gated rather than skipped silently — the gate is a property of the host, and the assertion
        // inside it is the one that proves records survive the projection.
        if (!TestEstate.LiveEstateAvailable)
        {
            return;
        }

        var live = TestEstate.Root;
        var workbook = Directory.EnumerateFiles(live, "NEXUS_DEVELOPMENT_CONTROL.xlsx", SearchOption.AllDirectories).FirstOrDefault();
        if (workbook is null)
        {
            return;
        }

        var projection = new DevelopmentControlReadProjection(workbook);
        var payload = projection.Project();

        Assert.NotEmpty(payload.WorkItems);
        Assert.All(payload.WorkItems, w => Assert.NotEmpty(w.WorkItemId));
        Assert.Equal(payload.WorkItems.Count,
            payload.WorkItems.Select(w => w.WorkItemId).Distinct(StringComparer.Ordinal).Count());
        Assert.NotEmpty(payload.ChangeScopes);
        Assert.NotEmpty(payload.LineageEdges);

        // And every fact is Authoritative: a workbook records, it does not observe.
        Assert.DoesNotContain(payload.WorkItems, w => w.Authority == DeliveryAuthorityClass.ObservedRuntime);
    }

    [Fact]
    public void No_published_fact_claims_runtime_observation()
    {
        WriteAuthoritativeWorkbook();
        Assert.True(Publisher().Publish(Destination, ObservedAt).Published);

        var payload = ReadPublished().Payload;

        // A workbook row is a record, not an observation that a process is running now. No runtime
        // source exists, so any ObservedRuntime value here would be a claim with nothing behind it.
        Assert.DoesNotContain(payload.WorkItems, w => w.Authority == DeliveryAuthorityClass.ObservedRuntime);
        Assert.DoesNotContain(payload.ChangeScopes, s => s.Authority == DeliveryAuthorityClass.ObservedRuntime);
        Assert.DoesNotContain(payload.LineageEdges, e => e.Authority == DeliveryAuthorityClass.ObservedRuntime);
        Assert.Equal(DeliveryAuthorityClass.Authoritative, payload.Control.DevelopmentControlAuthorityClass);
    }

    // ================================================================ refusals

    [Fact]
    public void A_missing_authority_is_REFUSED_and_nothing_is_published()
    {
        // The decisive refusal. An absent workbook must NOT yield an empty-but-valid snapshot: that
        // document says "DevelopmentControl holds no work items", which is a different and false claim
        // about an authority that could not be reached.
        var outcome = Publisher().Publish(Destination, ObservedAt);

        Assert.False(outcome.Published);
        Assert.Contains("AUTHORITY_UNAVAILABLE", outcome.Reason);
        Assert.False(File.Exists(PublishedPath), "an unreachable authority must publish no document at all");
    }

    [Fact]
    public void A_refused_publication_leaves_the_previous_snapshot_BYTE_IDENTICAL()
    {
        WriteAuthoritativeWorkbook();
        Assert.True(Publisher().Publish(Destination, ObservedAt).Published);
        var before = File.ReadAllBytes(PublishedPath);

        File.Delete(WorkbookPath);
        var outcome = Publisher().Publish(Destination, "2026-10-05T12:00:00Z");

        Assert.False(outcome.Published);
        Assert.Equal(before, File.ReadAllBytes(PublishedPath));
        Assert.False(File.Exists(PublishedPath + ".staging"), "a refusal must not leave a staging file behind");
    }

    [Fact]
    public void No_destination_is_refused_rather_than_guessed()
    {
        WriteAuthoritativeWorkbook();

        var outcome = Publisher().Publish("   ", ObservedAt);

        Assert.False(outcome.Published);
        Assert.Contains("explicitly", outcome.Reason);
    }

    [Fact]
    public void A_relative_destination_is_refused()
    {
        WriteAuthoritativeWorkbook();

        var outcome = Publisher().Publish("published", ObservedAt);

        Assert.False(outcome.Published);
        Assert.Contains("absolute", outcome.Reason);
    }

    [Fact]
    public void An_unparseable_observation_instant_is_refused()
    {
        WriteAuthoritativeWorkbook();

        var outcome = Publisher().Publish(Destination, "not-a-timestamp");

        Assert.False(outcome.Published);
        Assert.Contains("timestamp", outcome.Reason);
        Assert.False(File.Exists(PublishedPath));
    }

    // ================================================================ the digest

    [Fact]
    public void The_digest_does_NOT_move_when_only_the_instant_moves()
    {
        WriteAuthoritativeWorkbook();

        var first = Publisher().Publish(Destination, ObservedAt);
        var second = Publisher().Publish(Destination, "2026-10-05T09:30:00Z");

        Assert.True(first.Published, first.Reason);
        Assert.True(second.Published, second.Reason);
        // Two runs, two instants, identical authority state. A digest that moved would make a metadata
        // refresh indistinguishable from a state change — the exact confusion the digest exists to end.
        Assert.Equal(first.PayloadDigest, second.PayloadDigest);
    }

    [Fact]
    public void The_digest_DOES_move_when_authority_state_moves()
    {
        WriteAuthoritativeWorkbook();
        var before = Publisher().Publish(Destination, ObservedAt);
        Assert.True(before.Published, before.Reason);

        // A different authority state: the candidate form carries a different authority marker.
        WorkbookFixtureBuilder.Candidate(WorkbookPath);
        var after = Publisher().Publish(Destination, ObservedAt);

        Assert.True(after.Published, after.Reason);
        Assert.NotEqual(before.PayloadDigest, after.PayloadDigest);
    }

    [Fact]
    public void The_published_digest_is_reproducible_from_the_published_document()
    {
        WriteAuthoritativeWorkbook();
        var outcome = Publisher().Publish(Destination, ObservedAt);
        Assert.True(outcome.Published, outcome.Reason);

        var model = ReadPublished();

        // A consumer can recompute the fingerprint from the payload alone. If this ever fails, the
        // digest covers something the document does not carry, and no consumer can verify it.
        Assert.Equal(outcome.PayloadDigest, model.Source.PayloadDigest);
        Assert.Equal(outcome.PayloadDigest, DevelopmentControlReadProjection.SemanticDigest(model.Payload));
        Assert.Matches("^sha256:[a-f0-9]{64}$", model.Source.PayloadDigest);
    }

    // ================================================================ the boundary

    [Fact]
    public void Publishing_writes_NOTHING_into_the_authority_directory()
    {
        WriteAuthoritativeWorkbook();
        var before = File.ReadAllBytes(WorkbookPath);
        var written = File.GetLastWriteTimeUtc(WorkbookPath);
        var entriesBefore = Directory.GetFileSystemEntries(AuthorityDir).Length;

        Assert.True(Publisher().Publish(Destination, ObservedAt).Published);

        // Byte-identical, unretimed, and no new sibling. A lock file, a backup journal or a staging
        // file beside the workbook would each be a WRITE into the authority — publishing is a read,
        // and a publisher that needed the writer's lock to describe the authority would be claiming
        // the right to change it in order to report on it.
        Assert.Equal(before, File.ReadAllBytes(WorkbookPath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(WorkbookPath));
        Assert.Equal(entriesBefore, Directory.GetFileSystemEntries(AuthorityDir).Length);
    }

    [Fact]
    public void Publishing_twice_is_idempotent_and_leaves_no_staging_file()
    {
        WriteAuthoritativeWorkbook();

        Assert.True(Publisher().Publish(Destination, ObservedAt).Published);
        Assert.True(Publisher().Publish(Destination, ObservedAt).Published);

        Assert.True(File.Exists(PublishedPath));
        Assert.False(File.Exists(PublishedPath + ".staging"));
        Assert.Single(Directory.GetFiles(Destination));
    }

    [Fact]
    public void A_stale_staging_file_is_not_mistaken_for_a_publication()
    {
        WriteAuthoritativeWorkbook();
        Directory.CreateDirectory(Destination);
        File.WriteAllText(PublishedPath + ".staging", "{ not a publication }");

        var outcome = Publisher().Publish(Destination, ObservedAt);

        Assert.True(outcome.Published, outcome.Reason);
        using var document = JsonDocument.Parse(File.ReadAllText(PublishedPath));
        Assert.Equal(DevelopmentControlReadContract.SchemaVersion, document.RootElement.GetProperty("SchemaVersion").GetString());
    }

    // ================================================================ helpers

    private DevelopmentControlReadModel ReadPublished()
    {
        var json = File.ReadAllText(PublishedPath).TrimStart('﻿');
        return JsonSerializer.Deserialize<DevelopmentControlReadModel>(json)!;
    }
}
