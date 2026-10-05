using System.Security.Cryptography;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;

namespace Nexus.W96IdentityBackfill;

/// <summary>
/// W9.6 — two small drivers for the artifact-identity binding.
///
/// <list type="bullet">
/// <item><c>backfill &lt;storeRoot&gt;</c> — materialises a binding for every artifact a store already holds,
/// so artifacts published before W9.6 are protected too. Idempotent; never rewrites a binding.</item>
/// <item><c>publish &lt;storeRoot&gt; &lt;artifactId&gt; &lt;file&gt;</c> — attempts one publish and reports
/// the outcome. This exists so the W9.5 incident can be replayed as a LIVE control against a real store's
/// real bindings, rather than only as a unit test against a fixture.</item>
/// </list>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: W96IdentityBackfill backfill <storeRoot>");
            Console.Error.WriteLine("       W96IdentityBackfill publish  <storeRoot> <artifactId> <file> [buildId]");
            return 2;
        }

        var store = new FileArtifactStore(args[1]);

        return args[0] switch
        {
            "backfill" => await BackfillAsync(store).ConfigureAwait(false),
            "publish" when args.Length >= 4 => await PublishAsync(store, args[2], args[3], args.Length > 4 ? args[4] : "bld-36491ee979ed499c").ConfigureAwait(false),
            _ => 2
        };
    }

    private static async Task<int> BackfillAsync(FileArtifactStore store)
    {
        Console.WriteLine($"store  : {store.Root}");

        var created = await store.EnsureIdentityBindingsAsync().ConfigureAwait(false);

        Console.WriteLine($"created: {created.Count} binding(s)");

        foreach (var identity in created.OrderBy(i => i.ArtifactId.Value, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {identity.ArtifactId.Value}");
            Console.WriteLine($"    bound to {identity.ContentDigest}  build={identity.BuildId.Value}");
        }

        var artifactsDirectory = Path.Combine(store.Root, "artifacts");
        var identitiesDirectory = Path.Combine(store.Root, "identities");

        var artifacts = Directory.Exists(artifactsDirectory)
            ? Directory.EnumerateFiles(artifactsDirectory, "entry.json", SearchOption.AllDirectories).Count() : 0;
        var bound = Directory.Exists(identitiesDirectory)
            ? Directory.EnumerateFiles(identitiesDirectory, "*.json").Count() : 0;

        Console.WriteLine($"artifacts in store : {artifacts}");
        Console.WriteLine($"identity bindings  : {bound}");

        var complete = bound >= artifacts;
        Console.WriteLine($"all artifacts bound: {complete}");
        Console.WriteLine($"RESULT: {(complete ? "IDENTITY_BINDINGS_COMPLETE" : "IDENTITY_BINDINGS_INCOMPLETE")}");

        return complete ? 0 : 1;
    }

    private static async Task<int> PublishAsync(FileArtifactStore store, string artifactIdText, string file, string buildIdText)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"REFUSED: nothing to publish at '{file}'.");
            return 2;
        }

        ArtifactDigest digest;
        long size;

        using (var stream = File.OpenRead(file))
        {
            digest = ArtifactDigest.Compute(stream);
            size = stream.Length;
        }

        var artifact = new PackagedArtifact(
            ArtifactId.Parse(artifactIdText),
            digest,
            size,
            BuildId.Parse(buildIdText),
            Path.GetFileName(file));

        Console.WriteLine($"attempting : {artifactIdText}");
        Console.WriteLine($"payload    : {digest}  {size} bytes");

        var outcome = await store.PublishAsync(artifact, file).ConfigureAwait(false);

        Console.WriteLine($"accepted   : {outcome.IsAccepted}");
        Console.WriteLine($"already    : {outcome.IsAlreadyPresent}");
        Console.WriteLine($"refusal    : {outcome.RefusalReason}");
        Console.WriteLine($"detail     : {outcome.Detail}");
        Console.WriteLine($"RESULT: {(outcome.IsAccepted ? "PUBLISH_ACCEPTED" : "PUBLISH_REFUSED")}");

        return outcome.IsAccepted ? 0 : 1;
    }
}
