using System.Text.Json;
using Nexus.Delivery.Contracts.ReadModel;
using Nexus.Delivery.Core.ReadModel;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// W10.4 REMEDIATION — the atomic commit boundary under concurrent publication.
///
/// <para>
/// <b>Why this file exists.</b> A pre-existing parallel flake in this assembly was classified in W10.4
/// as <c>DELIVERY_PUBLISHER_TRANSIENT_COMMIT_DENIAL_UNDER_PARALLEL_LOAD</c>: an
/// <c>UnauthorizedAccessException</c> ("Access to the path is denied") raised at the stage-then-move
/// step of <see cref="DeliveryReadPublisher.Publish"/>, on the <b>second</b> publish — the one that
/// moves over a file the first had just written. It reproduced roughly one run in three under
/// whole-assembly parallel load, never in isolation, and never on Linux CI.
/// </para>
///
/// <para>
/// <b>These tests are the regression.</b> They reproduce the contention deterministically rather than
/// waiting for it to appear by chance: many publishers race one destination, and a reader runs
/// continuously against it. Every assertion is about an invariant that must hold regardless of who
/// wins — never "the retry eventually worked", which would be the retry-until-green shape this
/// remediation is explicitly forbidden from adopting.
/// </para>
///
/// <para>
/// <b>Non-vacuity.</b> The assertions below fail against the pre-fix commit. That is the point: a
/// stress test that passes before the fix proves nothing about the fix.
/// </para>
/// </summary>
public sealed class DeliveryAtomicCommitConcurrencyTests : IDisposable
{
    private readonly string _scratch;
    private readonly string _store;
    private readonly string _destination;

    private const string ObservedAt = "2026-10-05T12:00:00.0000000+00:00";

    public DeliveryAtomicCommitConcurrencyTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "w104-atomic-" + Guid.NewGuid().ToString("N")[..10]);
        _store = Path.Combine(_scratch, "store");
        _destination = Path.Combine(_scratch, "published");
        Directory.CreateDirectory(_store);
        Directory.CreateDirectory(_destination);
        SeedMinimalStore(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { /* temp; best effort */ }
    }

    private DeliveryReadPublisher Publisher() => new(new DeliveryReadProjection(_store));
    private string PublishedPath => Path.Combine(_destination, DeliveryReadContract.FileName);

    /// <summary>A store the publisher can project. Minimal but real: it must satisfy the contract.</summary>
    private static void SeedMinimalStore(string store)
    {
        Directory.CreateDirectory(Path.Combine(store, "releases"));
        Directory.CreateDirectory(Path.Combine(store, "deployments"));

        var releaseDir = Path.Combine(store, "releases", "rel-atomic");
        Directory.CreateDirectory(releaseDir);
        File.WriteAllText(Path.Combine(releaseDir, "entry.json"), """
        {
          "ReleaseId": "rel-atomic",
          "UnitId": "nexus.atomic",
          "Version": "1.0.0",
          "Lifecycle": "Registered",
          "BuildId": "bld-atomic",
          "SourceCommits": ["0123456789abcdef0123456789abcdef01234567"]
        }
        """);
    }

    // ================================================================ TASK 1 — the reproduction

    [Fact]
    public void ConcurrentPublishersToTheSameDestination_NeverCorruptOrLoseTheSnapshot()
    {
        // Twelve publishers, one destination, released at once by a barrier so they collide on the
        // commit boundary rather than arriving politely in sequence.
        const int publishers = 12;
        using var gate = new Barrier(publishers);

        var outcomes = new DeliveryReadPublishOutcome[publishers];
        var faults = new Exception?[publishers];

        Parallel.For(0, publishers, new ParallelOptions { MaxDegreeOfParallelism = publishers }, i =>
        {
            try
            {
                gate.SignalAndWait(TimeSpan.FromSeconds(30));
                outcomes[i] = Publisher().Publish(_destination, ObservedAt);
            }
            catch (Exception ex)
            {
                // A throw is a failure of the commit boundary, not an acceptable outcome: the contract
                // is that Publish RETURNS an outcome, refusing rather than raising.
                faults[i] = ex;
            }
        });

        var thrown = faults.Where(f => f is not null).ToArray();
        Assert.True(thrown.Length == 0,
            $"{thrown.Length} of {publishers} publishers threw instead of returning an outcome. First: "
            + $"{thrown.FirstOrDefault()?.GetType().Name}: {thrown.FirstOrDefault()?.Message}");

        // EVERY concurrent publisher must either commit or refuse explicitly. A refusal is acceptable;
        // an unexplained failure is not, and neither is silence.
        foreach (var outcome in outcomes)
        {
            Assert.NotNull(outcome);
            if (!outcome!.Published)
            {
                Assert.False(string.IsNullOrWhiteSpace(outcome.Reason),
                    "a refused publication must say why");
            }
        }

        // THE INVARIANT: after the dust settles the destination holds a complete, valid document — not
        // a partial one, not a mixture, and not nothing.
        Assert.True(File.Exists(PublishedPath),
            "at least one publisher committed, so the destination must hold a snapshot");

        var model = ReadPublishedWithoutExclusivity();
        Assert.Equal(DeliveryReadContract.SchemaVersion, model.SchemaVersion);
        Assert.NotNull(model.Payload);

        // Idempotence: twelve publishers of ONE authority state must produce ONE semantic digest.
        var digests = outcomes.Where(o => o!.Published).Select(o => o!.PayloadDigest).Distinct().ToArray();
        Assert.Single(digests);
    }

    [Fact]
    public void AReaderSeesOnlyCompleteSnapshots_WhilePublicationsCommitBeneathIt()
    {
        // TASK 5. A reader that does not cooperate — ordinary File.ReadAllText, no special share mode —
        // reading repeatedly while publishers commit beneath it. It must see a complete document or the
        // previous complete document, and never a partial, empty or torn one.
        //
        // The reader PAUSES between reads. That is deliberate and it is what TASK 5 specifies: "brief
        // legitimate read contention". A reader in a tight loop with no gap holds a deny-delete handle
        // continuously, and no File.Move-over can ever win against that on Windows — see
        // DenyDeleteReader_BlocksPublication_ButNeverCorruptsOrLoses_theSnapshot, which pins that
        // boundary explicitly instead of hiding it behind a reader that always succeeds.
        const int rounds = 60;
        var torn = new List<string>();
        var stop = false;

        Publisher().Publish(_destination, ObservedAt);

        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                try
                {
                    if (File.Exists(PublishedPath))
                    {
                        // Ordinary share mode — the reader makes no concession to the writer.
                        var text = File.ReadAllText(PublishedPath);
                        using var document = JsonDocument.Parse(text);
                        if (!document.RootElement.TryGetProperty("Payload", out _))
                        {
                            lock (torn) torn.Add("snapshot present but had no Payload");
                        }
                    }
                }
                catch (JsonException ex)
                {
                    lock (torn) torn.Add($"torn read: {ex.Message}");
                }
                catch (IOException)
                {
                    // A momentary handle is not corruption; the reader simply comes back.
                }

                // 5 ms, not 1 ms. Under whole-assembly parallel load a 1 ms sleep stretches and the
                // reader holds a deny-delete handle for sustained windows, so the publisher's 2-second
                // budget can legitimately expire — at which point it refuses CLEANLY, which is correct
                // behaviour and not a defect. That made the strict liveness assertion below fail for a
                // reason that had nothing to do with the fix. 5 ms keeps the contention genuinely
                // "brief" in TASK 5's sense while leaving the assertion strict.
                Thread.Sleep(5);
            }
        });

        for (var i = 0; i < rounds; i++)
        {
            var outcome = Publisher().Publish(_destination, ObservedAt);
            Assert.True(outcome.Published, $"round {i}: {outcome.Reason}");
        }

        Volatile.Write(ref stop, true);
        reader.Wait(TimeSpan.FromSeconds(30));

        Assert.True(torn.Count == 0,
            $"a reader observed {torn.Count} incomplete snapshot(s). First three: {string.Join(" | ", torn.Take(3))}");
    }

    [Fact]
    public void DenyDeleteReader_BlocksPublication_ButNeverCorruptsOrLoses_theSnapshot()
    {
        // THE BOUNDARY, PINNED. A consumer that opens the published file with FileShare.Read denies
        // delete, and on Windows a rename over such a handle cannot succeed. No atomic-replace
        // publisher can commit through that, and claiming otherwise would be the kind of unstated
        // assumption this programme exists to remove.
        //
        // What the publisher MUST do is exactly what is asserted here: refuse explicitly after its
        // bounded budget, leave the previous snapshot byte-identical, and leave no staging residue.
        // The failure is clean and reported, never silent and never corrupting.
        Assert.True(Publisher().Publish(_destination, ObservedAt).Published);
        var before = File.ReadAllBytes(PublishedPath);

        using var hold = new FileStream(PublishedPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var outcome = Publisher().Publish(_destination, "2026-10-07T00:00:00.0000000+00:00");

        Assert.False(outcome.Published, "a held deny-delete handle blocks the commit, and that is expected");
        Assert.Contains("gave up", outcome.Reason);
        Assert.Equal(before, File.ReadAllBytes(PublishedPath));
        Assert.Empty(Directory.GetFiles(_destination, "*.staging"));
    }

    [Fact]
    public void NoStagingResidueSurvivesASuccessfulPublication()
    {
        const int publishers = 8;
        Parallel.For(0, publishers, new ParallelOptions { MaxDegreeOfParallelism = publishers },
            _ => Publisher().Publish(_destination, ObservedAt));

        // TASK 6.8 — the commit boundary must not leave scratch files behind. The pre-fix code used ONE
        // fixed staging path shared by every publisher, so a loser could leave a file the winner never
        // moved, or delete a file another publisher was about to move.
        var residue = Directory.GetFiles(_destination, "*.staging");
        Assert.True(residue.Length == 0,
            $"{residue.Length} staging file(s) survived: {string.Join(", ", residue.Select(Path.GetFileName))}");
    }

    // ================================================================ helpers

    /// <summary>
    /// Reads the published snapshot with the MOST permissive sharing possible, so a transient handle
    /// held by a committing publisher cannot be mistaken for corruption of the content itself.
    /// </summary>
    private DeliveryReadModel ReadPublishedWithoutExclusivity()
    {
        using var stream = new FileStream(
            PublishedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<DeliveryReadModel>(stream)!;
    }
}
