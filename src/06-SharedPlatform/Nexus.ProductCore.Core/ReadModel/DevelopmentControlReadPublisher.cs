using System.Text;
using System.Text.Json;
using Nexus.ProductCore.Contracts.ReadModel;

namespace Nexus.ProductCore.Core.ReadModel;

/// <summary>What a publication attempt did, in one value a caller can act on.</summary>
public sealed record DevelopmentControlPublishOutcome(
    bool Published,
    string? Path,
    string PayloadDigest,
    string Reason)
{
    public static DevelopmentControlPublishOutcome Refused(string reason, string digest = "") =>
        new(false, null, digest, reason);
}

/// <summary>
/// W10.4 TASKs 6 and 7 — <b>publishes the DevelopmentControl read model atomically, to an explicit
/// destination.</b>
///
/// <para>
/// The rules are the ones W10.1 proved for Delivery, reused rather than re-derived: an explicit
/// destination, staging beside it and moving over it, refusals that replace nothing, and a semantic
/// digest over the payload alone so a metadata refresh is distinguishable from a state change.
/// </para>
///
/// <para>
/// <b>Read-only against the authority.</b> It opens the workbook for reading through the canonical
/// reader. It takes no lock, acquires no reservation, and never writes to the workbook or beside it.
/// A publisher that needed the writer lock to describe the authority would be claiming the right to
/// change it in order to report on it.
/// </para>
///
/// <para>
/// <b>The destination is a parameter, never a constant.</b> The consumer's location is a decision; a
/// caller that names nothing is refused rather than defaulted.
/// </para>
/// </summary>
public sealed class DevelopmentControlReadPublisher
{
    private readonly DevelopmentControlReadProjection _projection;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public DevelopmentControlReadPublisher(DevelopmentControlReadProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _projection = projection;
    }

    /// <summary>The digest a previous publication produced, or null when nothing is published.</summary>
    public static string? PublishedDigest(string destinationDirectory)
    {
        var path = Path.Combine(destinationDirectory, DevelopmentControlReadContract.FileName);
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

    /// <summary>Projects, validates and publishes. <paramref name="observedAt"/> is supplied so a run
    /// is reproducible for a fixed input and a fixed instant.</summary>
    public DevelopmentControlPublishOutcome Publish(string destinationDirectory, string observedAt, string? sourceRevision = null)
    {
        var destinationCheck = ValidateDestination(destinationDirectory);
        if (destinationCheck is not null)
        {
            return DevelopmentControlPublishOutcome.Refused(destinationCheck);
        }

        DevelopmentControlReadPayload payload;
        try
        {
            payload = _projection.Project();
        }
        catch (DevelopmentControlAuthorityUnavailableException ex)
        {
            return DevelopmentControlPublishOutcome.Refused($"AUTHORITY_UNAVAILABLE — {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return DevelopmentControlPublishOutcome.Refused($"the DevelopmentControl authority could not be read: {ex.Message}");
        }

        var digest = DevelopmentControlReadProjection.SemanticDigest(payload);

        var model = new DevelopmentControlReadModel(
            DevelopmentControlReadContract.SchemaVersion,
            new DevelopmentControlReadSource(
                DevelopmentControlReadContract.SchemaVersion,
                DevelopmentControlReadContract.Authority,
                sourceRevision ?? "(unspecified)",
                sourceRevision ?? "(unspecified)",
                observedAt,
                observedAt,
                digest),
            payload);

        var refusal = Validate(model);
        if (refusal is not null)
        {
            return DevelopmentControlPublishOutcome.Refused(refusal, digest);
        }

        var json = JsonSerializer.Serialize(model, WriteOptions);
        var finalPath = Path.Combine(destinationDirectory, DevelopmentControlReadContract.FileName);
        var stagingPath = finalPath + ".staging";

        try
        {
            File.WriteAllText(stagingPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(stagingPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            TryRemoveStaging(stagingPath);
            return DevelopmentControlPublishOutcome.Refused(
                $"publication failed while staging '{stagingPath}': {ex.Message}. The previously published "
                + "snapshot, if any, is unchanged.", digest);
        }

        return new DevelopmentControlPublishOutcome(true, finalPath, digest, "published");
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
            return $"'{destinationDirectory}' is not an absolute path. A relative destination resolves against "
                 + "the process's working directory, so two runs could publish to two places and neither would say so.";
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

    /// <summary>The contract's own preconditions, checked before anything is written.</summary>
    private static string? Validate(DevelopmentControlReadModel model)
    {
        if (!string.Equals(model.SchemaVersion, DevelopmentControlReadContract.SchemaVersion, StringComparison.Ordinal))
        {
            return $"the projection declared contract version '{model.SchemaVersion}', which is not "
                 + $"'{DevelopmentControlReadContract.SchemaVersion}'.";
        }

        if (string.IsNullOrWhiteSpace(model.Source.SourceId))
        {
            return "the projection does not identify its source.";
        }

        if (model.Source.PayloadDigest.Length != 71 || !model.Source.PayloadDigest.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return $"the payload digest '{model.Source.PayloadDigest}' is not a sha256: prefixed lowercase hex digest.";
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
            // Best effort. A leftover staging file is inert: it is not the published document.
        }
    }
}
