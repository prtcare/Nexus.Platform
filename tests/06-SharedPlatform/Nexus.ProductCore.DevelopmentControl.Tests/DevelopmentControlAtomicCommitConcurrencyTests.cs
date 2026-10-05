using System.Text.Json;
using Nexus.ProductCore.Contracts.ReadModel;
using Nexus.ProductCore.Core.ReadModel;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.4 REMEDIATION — the DevelopmentControl publisher's commit boundary under concurrent publication.
///
/// <para>
/// This publisher shared the Delivery publisher's defect: its staging path was the fixed string
/// <c>finalPath + ".staging"</c>, so two publishers writing to one destination shared a single scratch
/// file and one was denied. The defect was found in Delivery first because that is where the flake
/// appeared — it is the same defect in both, and leaving this sibling untested would have left it
/// waiting for its own parallel test to be written.
/// </para>
///
/// <para>
/// The two publishers cannot share a fix: they live in different assemblies, and
/// <c>Nexus.ProductCore.Core</c> has zero references by construction. The duplication is recorded in
/// the publisher's own source.
/// </para>
/// </summary>
public sealed class DevelopmentControlAtomicCommitConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "w104-dc-atomic-" + Guid.NewGuid().ToString("N")[..10]);

    private const string ObservedAt = "2026-10-05T00:00:00Z";

    public DevelopmentControlAtomicCommitConcurrencyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp; best effort */ }
    }

    private string WorkbookPath => Path.Combine(_root, "NEXUS_DEVELOPMENT_CONTROL.xlsx");
    private string Destination => Path.Combine(_root, "published");
    private string PublishedPath => Path.Combine(Destination, DevelopmentControlReadContract.FileName);

    private DevelopmentControlReadPublisher Publisher() =>
        new(new DevelopmentControlReadProjection(WorkbookPath));

    // ================================================================ concurrent publication

    [Fact]
    public void ConcurrentPublishersToTheSameDestination_CommitOrRefuseExplicitly_NeverThrow()
    {
        WorkbookFixtureBuilder.Authoritative(WorkbookPath);

        const int publishers = 12;
        using var gate = new Barrier(publishers);

        var outcomes = new DevelopmentControlPublishOutcome?[publishers];
        var faults = new Exception?[publishers];

        Parallel.For(0, publishers, new ParallelOptions { MaxDegreeOfParallelism = publishers }, i =>
        {
            try
            {
                gate.SignalAndWait(TimeSpan.FromSeconds(30));
                outcomes[i] = Publisher().Publish(Destination, ObservedAt);
            }
            catch (Exception ex) { faults[i] = ex; }
        });

        var thrown = faults.Where(f => f is not null).ToArray();
        Assert.True(thrown.Length == 0,
            $"{thrown.Length} of {publishers} publishers threw. First: "
            + $"{thrown.FirstOrDefault()?.GetType().Name}: {thrown.FirstOrDefault()?.Message}");

        foreach (var outcome in outcomes)
        {
            Assert.NotNull(outcome);
            if (!outcome!.Published)
            {
                Assert.False(string.IsNullOrWhiteSpace(outcome.Reason), "a refusal must say why");
            }
        }

        Assert.True(File.Exists(PublishedPath), "at least one publisher committed, so a snapshot must exist");

        using var stream = new FileStream(
            PublishedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var model = JsonSerializer.Deserialize<DevelopmentControlReadModel>(stream)!;
        Assert.Equal(DevelopmentControlReadContract.SchemaVersion, model.SchemaVersion);
        Assert.NotNull(model.Payload);

        var digests = outcomes.Where(o => o!.Published).Select(o => o!.PayloadDigest).Distinct().ToArray();
        Assert.Single(digests);
    }

    [Fact]
    public void NoStagingResidueSurvivesConcurrentPublication()
    {
        WorkbookFixtureBuilder.Authoritative(WorkbookPath);

        const int publishers = 8;
        Parallel.For(0, publishers, new ParallelOptions { MaxDegreeOfParallelism = publishers },
            _ => Publisher().Publish(Destination, ObservedAt));

        var residue = Directory.Exists(Destination) ? Directory.GetFiles(Destination, "*.staging") : [];
        Assert.True(residue.Length == 0,
            $"{residue.Length} staging file(s) survived: {string.Join(", ", residue.Select(Path.GetFileName))}");
    }

    [Fact]
    public void AReaderSeesOnlyCompleteSnapshots_WhilePublicationsCommitBeneathIt()
    {
        WorkbookFixtureBuilder.Authoritative(WorkbookPath);
        Assert.True(Publisher().Publish(Destination, ObservedAt).Published);

        const int rounds = 40;
        var torn = new List<string>();
        var stop = false;

        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                try
                {
                    if (!File.Exists(PublishedPath)) continue;
                    var text = File.ReadAllText(PublishedPath);
                    using var document = JsonDocument.Parse(text);
                    if (!document.RootElement.TryGetProperty("Payload", out _))
                    {
                        lock (torn) torn.Add("snapshot present but had no Payload");
                    }
                }
                catch (JsonException ex) { lock (torn) torn.Add($"torn read: {ex.Message}"); }
                catch (IOException) { /* momentary handle; not corruption */ }

                // Brief, not continuous. A reader holding a deny-delete handle with no gap can never be
                // committed over on Windows; TASK 5 specifies "brief legitimate read contention", and
                // the Delivery suite pins the deny-delete boundary explicitly rather than relying on a
                // reader that always happens to succeed.
                Thread.Sleep(1);
            }
        });

        for (var i = 0; i < rounds; i++)
        {
            var outcome = Publisher().Publish(Destination, ObservedAt);
            Assert.True(outcome.Published, $"round {i}: {outcome.Reason}");
        }

        Volatile.Write(ref stop, true);
        reader.Wait(TimeSpan.FromSeconds(30));

        Assert.True(torn.Count == 0,
            $"a reader observed {torn.Count} incomplete snapshot(s). First three: {string.Join(" | ", torn.Take(3))}");
    }
}
