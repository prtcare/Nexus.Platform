using System.Text;
using System.Text.Json;
using Nexus.Delivery.Contracts.ReadModel;

namespace Nexus.Delivery.Core.ReadModel;

/// <summary>What a publication attempt did, in one value a caller can act on.</summary>
public sealed record DeliveryReadPublishOutcome(
    bool Published,
    string? Path,
    string PayloadDigest,
    string Reason)
{
    public static DeliveryReadPublishOutcome Refused(string reason, string digest = "") =>
        new(false, null, digest, reason);
}

/// <summary>
/// W10.1 TASKs 7 and 8 — <b>publishes the Delivery read model atomically, to an explicit
/// destination.</b>
///
/// <para>
/// <b>The destination is a parameter, never a constant.</b> The consumer's location is not compiled
/// into Delivery: a hardcoded path would make the producer depend on one machine's layout, and the
/// ruling for this milestone requires an explicit, governed destination. A caller that names nothing
/// gets a refusal, not a default.
/// </para>
///
/// <para>
/// <b>Atomic by staging.</b> The document is written to a sibling staging file in the SAME directory
/// and then moved over the destination. Same-directory matters: a move across volumes is a copy, which
/// is not atomic. A reader therefore sees either the previous complete snapshot or the new complete
/// snapshot, and never a half-written file — including when the process dies mid-write, because the
/// staging file has not been moved.
/// </para>
///
/// <para>
/// <b>Refusals replace nothing.</b> Every failure path — unreadable authority, a projection that does
/// not satisfy the contract, an unusable destination — leaves the existing snapshot exactly as it
/// was. A publisher that cleared the destination and then failed would be worse than one that did
/// nothing at all.
/// </para>
///
/// <para>
/// <b>Idempotent by construction.</b> The payload digest excludes observation and publication
/// timestamps, so republishing unchanged authority state produces the same semantic digest while the
/// metadata moves. <see cref="PublishedDigest"/> lets a caller see whether the state actually changed.
/// </para>
/// </summary>
public sealed class DeliveryReadPublisher
{
    private readonly DeliveryReadProjection _projection;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public DeliveryReadPublisher(DeliveryReadProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _projection = projection;
    }

    /// <summary>The digest a previous publication produced, or null when nothing is published.</summary>
    public static string? PublishedDigest(string destinationDirectory)
    {
        var path = Path.Combine(destinationDirectory, DeliveryReadContract.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path).TrimStart('﻿'));
            return document.RootElement.TryGetProperty("Source", out var source)
                && source.TryGetProperty("PayloadDigest", out var digest)
                ? digest.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Projects, validates and publishes. <paramref name="observedAt"/> is supplied by the caller so
    /// a projection is reproducible for a fixed input and a fixed instant.
    /// </summary>
    public DeliveryReadPublishOutcome Publish(string destinationDirectory, string observedAt, string? sourceRevision = null)
    {
        var destinationCheck = ValidateDestination(destinationDirectory);
        if (destinationCheck is not null)
        {
            return DeliveryReadPublishOutcome.Refused(destinationCheck);
        }

        DeliveryReadPayload payload;
        try
        {
            payload = _projection.Project();
        }
        catch (DeliveryAuthorityUnavailableException ex)
        {
            // The authority is not there. Nothing is published and nothing is replaced — and in
            // particular no empty document is published over a good previous one.
            return DeliveryReadPublishOutcome.Refused($"AUTHORITY_UNAVAILABLE — {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // The authority could not be read. Nothing is published and nothing is replaced.
            return DeliveryReadPublishOutcome.Refused($"the Delivery authority could not be read: {ex.Message}");
        }

        var digest = DeliveryReadProjection.SemanticDigest(payload);

        var model = new DeliveryReadModel(
            DeliveryReadContract.SchemaVersion,
            new DeliveryReadSource(
                DeliveryReadContract.SchemaVersion,
                DeliveryReadContract.Authority,
                sourceRevision ?? "(unspecified)",
                sourceRevision ?? "(unspecified)",
                observedAt,
                observedAt,
                digest),
            payload);

        var refusal = Validate(model);
        if (refusal is not null)
        {
            return DeliveryReadPublishOutcome.Refused(refusal, digest);
        }

        var json = JsonSerializer.Serialize(model, WriteOptions);
        var finalPath = Path.Combine(destinationDirectory, DeliveryReadContract.FileName);
        var stagingPath = finalPath + ".staging";

        try
        {
            // Stage beside the destination, then move over it. The move is the commit point.
            File.WriteAllText(stagingPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(stagingPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            TryRemoveStaging(stagingPath);

            // The destination was not replaced. Whatever was published before is still published.
            return DeliveryReadPublishOutcome.Refused(
                $"publication failed while staging '{stagingPath}': {ex.Message}. The previously published "
                + "snapshot, if any, is unchanged.", digest);
        }

        return new DeliveryReadPublishOutcome(true, finalPath, digest, "published");
    }

    private static string? ValidateDestination(string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return "no destination was named. The published location is a governed decision and is never "
                 + "guessed: pass one explicitly.";
        }

        if (!Path.IsPathRooted(destinationDirectory))
        {
            return $"'{destinationDirectory}' is not an absolute path. A relative destination resolves "
                 + "against the process's working directory, so two runs could publish to two places "
                 + "and neither would say so.";
        }

        try
        {
            Directory.CreateDirectory(destinationDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the destination '{destinationDirectory}' could not be used: {ex.Message}";
        }

        return null;
    }

    /// <summary>
    /// The contract's own preconditions, checked before anything is written.
    ///
    /// <para>
    /// These are the checks a consumer would make — version recognised, digest well-formed, source
    /// identified. Making them here means a document that would be refused downstream is never
    /// published, so a consumer never has to treat "the file exists" as "the file is good".
    /// </para>
    /// </summary>
    private static string? Validate(DeliveryReadModel model)
    {
        if (!string.Equals(model.SchemaVersion, DeliveryReadContract.SchemaVersion, StringComparison.Ordinal))
        {
            return $"the projection declared contract version '{model.SchemaVersion}', which is not "
                 + $"'{DeliveryReadContract.SchemaVersion}'. A consumer refuses an unrecognised version, "
                 + "and publishing one would take the feed dark.";
        }

        if (string.IsNullOrWhiteSpace(model.Source.SourceId))
        {
            return "the projection does not identify its source. A consumer cannot attribute a fact it "
                 + "cannot trace.";
        }

        if (model.Source.PayloadDigest.Length != 71 || !model.Source.PayloadDigest.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return $"the payload digest '{model.Source.PayloadDigest}' is not a sha256: prefixed "
                 + "lowercase hex digest.";
        }

        if (!DateTimeOffset.TryParse(model.Source.ObservedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out _))
        {
            return $"the observation instant '{model.Source.ObservedAt}' is not a parseable timestamp.";
        }

        return null;
    }

    private static void TryRemoveStaging(string stagingPath)
    {
        try
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
        catch (IOException)
        {
            // Best effort. A leftover staging file is inert: it is not the published document, and
            // the next publication overwrites it.
        }
    }
}
