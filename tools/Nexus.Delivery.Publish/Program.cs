using System.Globalization;
using Nexus.Delivery.Contracts.ReadModel;
using Nexus.Delivery.Core.ReadModel;

namespace Nexus.Delivery.PublishTool;

/// <summary>
/// W10.1 TASK 7 — <b>the governed Delivery read-model publisher.</b>
///
/// <code>
///   Nexus.Delivery.Publish publish &lt;storeRoot&gt; &lt;destinationDirectory&gt; [observedAtUtc]
///   Nexus.Delivery.Publish digest  &lt;storeRoot&gt;
/// </code>
///
/// <para>
/// <b>Both roots are named by the caller.</b> The authority location and the publication destination
/// are decisions, not constants: nothing in this tool knows where the estate is or where a consumer
/// reads from. Local, offline use is the ordinary case — a store on disk and a directory beside it.
/// </para>
///
/// <para>
/// <b>Exit codes carry the outcome,</b> because a publication that failed must not look like one that
/// succeeded to whatever is driving this:
/// </para>
/// <list type="table">
/// <item><description><c>0</c> — published (or the digest was reported).</description></item>
/// <item><description><c>2</c> — usage error.</description></item>
/// <item><description><c>3</c> — the authority could not be read.</description></item>
/// <item><description><c>4</c> — the projection did not satisfy the contract; nothing was published.</description></item>
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
        var storeRoot = args[1];

        if (!Directory.Exists(storeRoot))
        {
            Console.Error.WriteLine(
                $"AUTHORITY_UNAVAILABLE — no Delivery store exists at '{storeRoot}'. Nothing was "
                + "published. A missing authority is not an empty authority, and is not reported as one.");
            return 3;
        }

        var projection = new DeliveryReadProjection(storeRoot);

        switch (verb)
        {
            case "digest":
            {
                try
                {
                    var digest = DeliveryReadProjection.SemanticDigest(projection.Project());
                    Console.WriteLine(digest);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"AUTHORITY_UNREADABLE — {ex.Message}");
                    return 3;
                }
            }

            case "publish":
            {
                if (args.Length < 3)
                {
                    Usage();
                    return 2;
                }

                var destination = args[2];

                // The observation instant is injectable so a run is reproducible; the default is
                // "now", which is the only place this tool reads a clock.
                var observedAt = args.Length > 3
                    ? args[3]
                    : DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                var before = DeliveryReadPublisher.PublishedDigest(destination);

                var publisher = new DeliveryReadPublisher(projection);
                var outcome = publisher.Publish(destination, observedAt, sourceRevision: $"store:{Path.GetFullPath(storeRoot)}");

                if (!outcome.Published)
                {
                    Console.Error.WriteLine($"PUBLICATION_REFUSED — {outcome.Reason}");
                    return outcome.Reason.Contains("destination", StringComparison.OrdinalIgnoreCase) ? 5 : 4;
                }

                // A metadata refresh and an actual state change are different events, and the digest
                // is what tells them apart. Reporting the comparison is the point of publishing the
                // digest rather than only the document.
                var change = before switch
                {
                    null => "first publication",
                    var b when string.Equals(b, outcome.PayloadDigest, StringComparison.Ordinal) => "NO STATE CHANGE — metadata refresh only",
                    _ => "DELIVERY STATE CHANGED"
                };

                Console.WriteLine($"published  : {outcome.Path}");
                Console.WriteLine($"digest     : {outcome.PayloadDigest}");
                Console.WriteLine($"previous   : {before ?? "(none)"}");
                Console.WriteLine($"comparison : {change}");
                Console.WriteLine($"observedAt : {observedAt}");

                return 0;
            }

            default:
                Usage();
                return 2;
        }
    }

    private static void Usage() => Console.Error.WriteLine(
        "usage: Nexus.Delivery.Publish publish <storeRoot> <destinationDirectory> [observedAtUtc]\n"
        + "       Nexus.Delivery.Publish digest  <storeRoot>");
}
