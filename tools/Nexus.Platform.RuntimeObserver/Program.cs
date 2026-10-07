using System.Globalization;
using Nexus.ProductCore.Contracts.ReadModel;
using Nexus.ProductCore.Core.ReadModel;

namespace Nexus.Platform.RuntimeObserverTool;

/// <summary>
/// W10.6 TASK 6 — <b>the governed Platform runtime observer.</b>
///
/// <code>
///   Nexus.Platform.RuntimeObserver observe &lt;repositoryRoot&gt; &lt;destinationDirectory&gt; [observedAtUtc]
///   Nexus.Platform.RuntimeObserver digest  &lt;repositoryRoot&gt;
/// </code>
///
/// <para>
/// <b>A ONE-SHOT SNAPSHOT, NOT A DAEMON, and that is TASK 6's own preference applied to this
/// repository.</b> The directive says to prefer snapshot observation "unless a canonical Platform host
/// already exists and clearly owns this responsibility". Measured, there is no such host: Platform has
/// zero persistent runtime units, and the only host it ever owned was retired on 2026-10-05. Standing
/// up a daemon to observe a repository with nothing running would be manufacturing a process so that
/// something could report on there being no processes.
/// </para>
///
/// <para>
/// <b>The observer is itself the thing it measures, and that is the honest shape.</b> Platform's only
/// runnable artifacts are one-shot command tools; this observer is one more of them, and it is
/// classified as one by its own map.
/// </para>
///
/// <para>
/// <b>It publishes; it does not observe Atlas's behalf.</b> Everything it writes is the versioned
/// contract. Atlas reads that file and never enumerates a host.
/// </para>
///
/// <para><b>Exit codes</b>, because a publication that failed must not look like one that succeeded:</para>
/// <list type="table">
/// <item><description><c>0</c> — observed and published (or the digest was reported).</description></item>
/// <item><description><c>2</c> — usage error.</description></item>
/// <item><description><c>3</c> — the governed repository could not be read.</description></item>
/// <item><description><c>4</c> — the observation did not satisfy the contract; nothing was published.</description></item>
/// <item><description><c>5</c> — the destination was unusable; the previous snapshot is unchanged.</description></item>
/// </list>
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Usage();
            return 2;
        }

        var verb = args[0].ToLowerInvariant();
        var repositoryRoot = args[1];

        if (!Directory.Exists(repositoryRoot))
        {
            Console.Error.WriteLine(
                $"REPOSITORY_UNAVAILABLE — no directory exists at '{repositoryRoot}'. The applicability map "
                + "is derived from the governed solution, so without it there is nothing to observe. "
                + "Nothing was published and no empty map was substituted for it.");
            return 3;
        }

        var observation = new PlatformRuntimeObservation(repositoryRoot);

        if (verb == "digest")
        {
            try
            {
                var payload = observation.Observe(Timestamp());
                Console.WriteLine(PlatformRuntimeObservation.SemanticDigest(payload));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"REPOSITORY_UNREADABLE — {ex.Message}");
                return 3;
            }
        }

        if (verb != "observe" || args.Length < 3)
        {
            Usage();
            return 2;
        }

        var destination = args[2];
        var observedAt = args.Length > 3 ? args[3] : Timestamp();

        var publisher = new PlatformRuntimeReadPublisher(observation);
        var outcome = publisher.Publish(destination, observedAt, sourceRevision: repositoryRoot);

        if (!outcome.Published)
        {
            Console.Error.WriteLine(outcome.Reason);
            return outcome.Reason.Contains("destination", StringComparison.OrdinalIgnoreCase) ? 5 : 4;
        }

        Console.WriteLine($"published  : {outcome.Path}");
        Console.WriteLine($"digest     : {outcome.PayloadDigest}");
        Console.WriteLine($"observedAt : {observedAt}");

        // A short, deliberately NON-SENSITIVE summary. No paths, no host names, no process inventory —
        // the counts are the whole point, and an operator can read the rest out of the publication.
        var summary = observation.Observe(observedAt);
        foreach (var group in summary.Units
                     .GroupBy(u => u.RuntimeApplicability, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {group.Key,-12} {group.Count()}");
        }

        Console.WriteLine($"  gaps         {summary.Gaps.Count}");
        return 0;
    }

    private static string Timestamp() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static void Usage()
    {
        Console.Error.WriteLine("""
            usage:
              Nexus.Platform.RuntimeObserver observe <repositoryRoot> <destinationDirectory> [observedAtUtc]
              Nexus.Platform.RuntimeObserver digest  <repositoryRoot>

            The observer reads the governed solution and one kernel-established liveness fact. It does
            not enumerate processes, does not probe an endpoint, and starts nothing.
            """);
    }
}
