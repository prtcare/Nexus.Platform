using System.Globalization;
using Nexus.ProductCore.Contracts.ReadModel;
using Nexus.ProductCore.Core.ReadModel;

namespace Nexus.DevelopmentControl.PublishTool;

/// <summary>
/// W10.4 TASK 6 — the governed DevelopmentControl read-model publisher.
///
///   Nexus.DevelopmentControl.Publish publish &lt;workbookPath&gt; &lt;destinationDirectory&gt; [observedAtUtc]
///   Nexus.DevelopmentControl.Publish digest  &lt;workbookPath&gt;
///
/// Exit codes: 0 published, 2 usage, 3 authority unavailable or unreadable, 4 the projection did not
/// satisfy the contract, 5 the destination was unusable.
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
        var workbookPath = args[1];

        if (!File.Exists(workbookPath))
        {
            Console.Error.WriteLine(
                $"AUTHORITY_UNAVAILABLE — no DevelopmentControl workbook exists at '{workbookPath}'. Nothing "
                + "was published, and no empty dataset was substituted for it.");
            return 3;
        }

        var projection = new DevelopmentControlReadProjection(workbookPath);

        if (verb == "digest")
        {
            try
            {
                Console.WriteLine(DevelopmentControlReadProjection.SemanticDigest(projection.Project()));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"AUTHORITY_UNREADABLE — {ex.Message}");
                return 3;
            }
        }

        if (verb != "publish" || args.Length < 3)
        {
            Usage();
            return 2;
        }

        var destination = args[2];
        var observedAt = args.Length > 3
            ? args[3]
            : DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var before = DevelopmentControlReadPublisher.PublishedDigest(destination);
        var outcome = new DevelopmentControlReadPublisher(projection)
            .Publish(destination, observedAt, sourceRevision: $"workbook:{Path.GetFullPath(workbookPath)}");

        if (!outcome.Published)
        {
            Console.Error.WriteLine($"PUBLICATION_REFUSED — {outcome.Reason}");
            return outcome.Reason.Contains("destination", StringComparison.OrdinalIgnoreCase) ? 5 : 4;
        }

        var change = before switch
        {
            null => "first publication",
            var b when string.Equals(b, outcome.PayloadDigest, StringComparison.Ordinal) => "NO STATE CHANGE — metadata refresh only",
            _ => "DEVELOPMENTCONTROL STATE CHANGED"
        };

        Console.WriteLine($"published  : {outcome.Path}");
        Console.WriteLine($"digest     : {outcome.PayloadDigest}");
        Console.WriteLine($"previous   : {before ?? "(none)"}");
        Console.WriteLine($"comparison : {change}");
        Console.WriteLine($"observedAt : {observedAt}");
        return 0;
    }

    private static void Usage() => Console.Error.WriteLine(
        "usage: Nexus.DevelopmentControl.Publish publish <workbookPath> <destinationDirectory> [observedAtUtc]\n"
        + "       Nexus.DevelopmentControl.Publish digest  <workbookPath>");
}
