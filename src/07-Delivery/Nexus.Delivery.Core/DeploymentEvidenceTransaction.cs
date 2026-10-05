using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>Where a staged evidence record sits relative to the transition it describes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeploymentEvidenceState
{
    /// <summary>Persisted, and the transition it describes has not been attempted yet.</summary>
    PendingTransition,

    /// <summary>The transition committed. <see cref="DeploymentEvidenceEnvelope.LineageId"/> names the record.</summary>
    Committed,

    /// <summary>
    /// The transition was attempted and failed. The evidence exists, and it says the deployment did not
    /// complete — which is the whole point of writing it first.
    /// </summary>
    CommitFailed
}

/// <summary>What <see cref="DeploymentEvidenceTransaction.StageAsync"/> did with the file it was given.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeploymentEvidenceStaging
{
    /// <summary>The evidence did not exist and was written.</summary>
    Created,

    /// <summary>
    /// Byte-equal evidence for this same attempt was already on disk. Nothing was written — the existing
    /// envelope stands. A re-run of identical inputs is a no-op by design, not a second record.
    /// </summary>
    Existing
}

/// <summary>
/// The durable record of one deployment step, written <b>before</b> the step is committed, and
/// <b>addressed by the attempt it belongs to</b> rather than by the environment it happened in.
///
/// <para>
/// <b>Why the order is the feature.</b> W9.4's <c>verify</c> run appended <c>L-W9-3</c> to the governed
/// ledger and then threw at teardown; <c>DEV_VERIFICATION.json</c> was never written. The ledger advanced
/// with nothing beside it, and the only trace was a file that was not there.
/// </para>
///
/// <para>
/// Staging first inverts the failure mode. Every exit from this class leaves the evidence on disk and
/// truthful: <see cref="DeploymentEvidenceState.PendingTransition"/> if the process died before committing,
/// <see cref="DeploymentEvidenceState.CommitFailed"/> if the commit threw, and
/// <see cref="DeploymentEvidenceState.Committed"/> only once the ledger has accepted the record.
/// </para>
///
/// <para>
/// <b>Why the address changed.</b> The first version wrote one file per <i>environment</i> —
/// <c>&lt;environment&gt;/DEV_VERIFICATION.json</c> — so every verification attempt overwrote the one before
/// it. Three attempts were made during the W9.4 remediation and only the last survived; the envelopes for
/// <c>L-W9-4</c> and <c>L-W9-5</c> are unrecoverable, which is recorded in the W9.4 addendum as
/// <c>HISTORICAL_EVIDENCE_OVERWRITE_DEFECT</c>. Evidence addressed by environment is evidence that a later
/// run can destroy, and a record that a later run can destroy is not immutable.
/// </para>
///
/// <para>
/// The address is now derived from the identities the contract already has:
/// <c>&lt;root&gt;/deployments/&lt;DeploymentId&gt;/attempts/&lt;attempt&gt;/&lt;file&gt;</c>. The
/// <see cref="DeploymentId"/> says which deployment; the attempt ordinal says which verification of it.
/// <b>Neither is a timestamp.</b> A clock is not an identity — two runs in the same second would collide —
/// and the deployment and attempt ordinals are already typed, derived from the governed ledger, and
/// reproducible on another host.
/// </para>
/// </summary>
public sealed class DeploymentEvidenceTransaction
{
    private const string Schema = "nexus-deployment-evidence-v1";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directory;
    private readonly string _fileName;

    public DeploymentEvidenceTransaction(string evidenceDirectory, string evidenceFileName)
    {
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            throw new ArgumentException("Evidence needs a directory.", nameof(evidenceDirectory));
        }

        if (string.IsNullOrWhiteSpace(evidenceFileName))
        {
            throw new ArgumentException("Evidence needs a file name.", nameof(evidenceFileName));
        }

        _directory = Path.GetFullPath(evidenceDirectory);
        _fileName = evidenceFileName;
    }

    /// <summary>
    /// The immutable directory for one attempt:
    /// <c>&lt;root&gt;/deployments/&lt;DeploymentId&gt;/attempts/&lt;attempt&gt;</c>.
    ///
    /// <para>
    /// A pure function of two typed identities, so any reader holding the envelope — or the lineage record
    /// it names — can resolve the correct file without being told where it is. That is what
    /// "enough identity to resolve the correct envelope" means in practice.
    /// </para>
    /// </summary>
    public static string DirectoryFor(string evidenceRoot, DeploymentId deployment, int attempt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        ArgumentNullException.ThrowIfNull(deployment);

        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attempt), attempt, "Verification attempts are numbered from 1.");
        }

        return Path.Combine(
            Path.GetFullPath(evidenceRoot),
            "deployments",
            deployment.Value,
            "attempts",
            attempt.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Convenience: the transaction that owns one attempt's evidence.</summary>
    public static DeploymentEvidenceTransaction ForAttempt(
        string evidenceRoot,
        DeploymentId deployment,
        int attempt)
        => new(DirectoryFor(evidenceRoot, deployment, attempt), FileNameFor(deployment));

    /// <summary>
    /// The envelope's file name, <b>a pure function of the deployment identity</b> rather than a parameter.
    ///
    /// <para>
    /// <b>Why this is derived and not supplied.</b> W9.4's version took the name as an optional argument
    /// defaulting to <c>DEV_VERIFICATION.json</c>. That was correct while ENV-DEV was the only environment the
    /// driver could reach; under a second environment the default becomes a trap, because the one thing a
    /// caller must not do — write a DEV-named envelope into a TEST deployment's directory — is exactly what
    /// omitting the argument would do, silently. The estate has a name for that: a value that defaults to the
    /// passing case. The environment is already in the <see cref="DeploymentId"/>, so deriving the name from it
    /// removes the possibility of the two disagreeing rather than making the disagreement a caller error.
    /// </para>
    ///
    /// <para>
    /// It reads the <b>id's environment</b>, never a separate argument, so an envelope's name and its address
    /// are the same fact stated twice. A DEV deployment cannot produce a <c>TEST_VERIFICATION.json</c>, and a
    /// TEST deployment cannot produce a <c>DEV_VERIFICATION.json</c>, by construction.
    /// </para>
    /// </summary>
    public static string FileNameFor(DeploymentId deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.Environment.Value switch
        {
            DeploymentEnvironmentId.Dev => "DEV_VERIFICATION.json",
            DeploymentEnvironmentId.Test => "TEST_VERIFICATION.json",
            DeploymentEnvironmentId.Prod => "PROD_VERIFICATION.json",
            var other => throw new ArgumentException(
                $"'{other}' is not a ratified environment, so no evidence file name can be derived for it.",
                nameof(deployment))
        };
    }

    public string EvidencePath => Path.Combine(_directory, _fileName);

    /// <summary>
    /// Persists the evidence and returns it, or throws — in which case <b>the caller must not attempt the
    /// transition</b>. That is not a convention: <see cref="CommitAsync"/> only accepts a
    /// <see cref="StagedEvidence"/>, and the only way to obtain one is for this method to have returned.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Evidence already exists at this attempt's path and is <b>not</b> equal to what is being staged.
    /// Two different evidences for one attempt is a contradiction, and the later one does not get to
    /// overwrite the earlier — that is the defect this address exists to remove.
    /// </exception>
    public async Task<StagedEvidence> StageAsync(
        string releaseId,
        DeploymentEnvironmentId environment,
        DeploymentId deployment,
        int attempt,
        string intendedLineageId,
        IReadOnlyDictionary<string, object?> facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseId);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrWhiteSpace(intendedLineageId);
        ArgumentNullException.ThrowIfNull(facts);

        if (deployment.Environment != environment)
        {
            throw new ArgumentException(
                $"The deployment id '{deployment.Value}' names {deployment.Environment}, but the evidence is for {environment}.",
                nameof(deployment));
        }

        var envelope = new DeploymentEvidenceEnvelope(
            Schema,
            releaseId,
            environment.Value,
            intendedLineageId,
            DeploymentEvidenceState.PendingTransition,
            LineageId: null,
            CommitDetail: null,
            StagedAtUtc: DateTimeOffset.UtcNow,
            CommittedAtUtc: null,
            facts,
            deployment.Value,
            attempt);

        if (File.Exists(EvidencePath))
        {
            var existing = Read(EvidencePath);

            // Idempotent ONLY when the identity and the facts agree. The timestamp is deliberately excluded:
            // a re-run of identical inputs must be a no-op, and comparing timestamps would make every
            // re-run look like different evidence.
            if (existing.MatchesEvidenceOf(envelope))
            {
                return new StagedEvidence(EvidencePath, existing, DeploymentEvidenceStaging.Existing);
            }

            throw new InvalidOperationException(
                $"Evidence already exists for {deployment.Value} attempt {attempt} at '{EvidencePath}' and differs from what is being staged "
                + $"(existing intended lineage id '{existing.IntendedLineageId}', state {existing.State}; staged intended lineage id '{intendedLineageId}'). "
                + "An attempt's evidence is immutable. A genuine repeat attempt is a NEW attempt number, which writes to a new path; it does not overwrite this one.");
        }

        await WriteAsync(envelope, cancellationToken).ConfigureAwait(false);

        return new StagedEvidence(EvidencePath, envelope, DeploymentEvidenceStaging.Created);
    }

    /// <summary>
    /// Runs <paramref name="commit"/> and seals the staged evidence with its outcome.
    ///
    /// <para>
    /// <b>The commit delegate is invoked exactly once and never before the evidence exists.</b> On success
    /// the envelope becomes <see cref="DeploymentEvidenceState.Committed"/> naming the committed
    /// <c>LineageId</c>; on failure it becomes <see cref="DeploymentEvidenceState.CommitFailed"/> carrying
    /// the failure's own message, and the exception is rethrown. Either way the file is on disk before,
    /// during and after.
    /// </para>
    ///
    /// <para>
    /// <b>A committed envelope is never resealed over.</b> If the file already records a commit, this
    /// refuses rather than rewriting history — the same rule that stops a second, contradictory lineage
    /// record being appended.
    /// </para>
    /// </summary>
    public async Task<DeploymentEvidenceEnvelope> CommitAsync(
        StagedEvidence staged,
        Func<Task<string>> commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(commit);

        var current = Read(staged.Path);

        if (current.State == DeploymentEvidenceState.Committed)
        {
            throw new InvalidOperationException(
                $"The evidence at '{staged.Path}' is already sealed as Committed ('{current.LineageId}'). "
                + "A sealed envelope is immutable; a repeat attempt writes to a new attempt path.");
        }

        string lineageId;

        try
        {
            lineageId = await commit().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteAsync(
                current with
                {
                    State = DeploymentEvidenceState.CommitFailed,
                    CommitDetail = $"{ex.GetType().Name}: {ex.Message}"
                },
                cancellationToken).ConfigureAwait(false);

            throw;
        }

        var committed = current with
        {
            State = DeploymentEvidenceState.Committed,
            LineageId = lineageId,
            CommitDetail = $"The transition committed as '{lineageId}'.",
            CommittedAtUtc = DateTimeOffset.UtcNow
        };

        await WriteAsync(committed, cancellationToken).ConfigureAwait(false);

        return committed;
    }

    /// <summary>Reads an envelope back from disk. Used by tests and by any reader that must not trust memory.</summary>
    public DeploymentEvidenceEnvelope Read(string path)
    {
        var envelope = JsonSerializer.Deserialize<DeploymentEvidenceEnvelope>(File.ReadAllText(path), Options);

        return envelope
            ?? throw new InvalidOperationException($"The evidence record at '{path}' decoded to nothing.");
    }

    private async Task WriteAsync(DeploymentEvidenceEnvelope envelope, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);

        var json = JsonSerializer.Serialize(envelope, Options);
        var path = EvidencePath;
        var temporary = path + ".tmp";

        await File.WriteAllTextAsync(
            temporary,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);

        // The move is the commit point of the FILE. A reader therefore sees either the previous envelope or
        // the new one, never a half-written one.
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>A staged envelope, and the only thing <see cref="DeploymentEvidenceTransaction.CommitAsync"/> accepts.</summary>
public sealed record StagedEvidence(string Path, DeploymentEvidenceEnvelope Envelope, DeploymentEvidenceStaging Staging);

/// <summary>The persisted shape. Every member is written, so a reader never has to infer one from absence.</summary>
public sealed record DeploymentEvidenceEnvelope(
    [property: JsonPropertyOrder(0)] string Schema,
    [property: JsonPropertyOrder(1)] string ReleaseId,
    [property: JsonPropertyOrder(2)] string Environment,
    [property: JsonPropertyOrder(3)] string IntendedLineageId,
    [property: JsonPropertyOrder(4)] DeploymentEvidenceState State,
    [property: JsonPropertyOrder(5)] string? LineageId,
    [property: JsonPropertyOrder(6)] string? CommitDetail,
    [property: JsonPropertyOrder(7)] DateTimeOffset StagedAtUtc,
    [property: JsonPropertyOrder(8)] DateTimeOffset? CommittedAtUtc,
    [property: JsonPropertyOrder(9)] IReadOnlyDictionary<string, object?> Facts,
    [property: JsonPropertyOrder(10)] string? DeploymentId = null,
    [property: JsonPropertyOrder(11)] int Attempt = 0)
{
    /// <summary>
    /// True when this envelope and <paramref name="other"/> describe the same evidence for the same attempt.
    /// Timestamps are excluded on purpose — see <see cref="DeploymentEvidenceTransaction.StageAsync"/>.
    /// </summary>
    public bool MatchesEvidenceOf(DeploymentEvidenceEnvelope other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(ReleaseId, other.ReleaseId, StringComparison.Ordinal)
            && string.Equals(Environment, other.Environment, StringComparison.Ordinal)
            && string.Equals(DeploymentId, other.DeploymentId, StringComparison.Ordinal)
            && Attempt == other.Attempt
            && string.Equals(IntendedLineageId, other.IntendedLineageId, StringComparison.Ordinal)
            && string.Equals(FactsDigest().ToString(), other.FactsDigest().ToString(), StringComparison.Ordinal);
    }

    /// <summary>A digest over the FACTS alone — the part that must not vary between two runs of one attempt.</summary>
    public ArtifactDigest FactsDigest()
    {
        var builder = new StringBuilder();

        foreach (var key in Facts.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            builder.Append(key).Append('\0')
                .Append(JsonSerializer.Serialize(Facts[key])).Append('\0');
        }

        return ArtifactDigest.Compute(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    /// <summary>
    /// The envelope in a form that changes when any member changes, so a reader can tell whether the
    /// evidence it holds is the one a transition was decided on. Deliberately excludes nothing — an
    /// envelope digest that skipped the state would be equal for a pending and a failed record.
    /// </summary>
    public string CanonicalForm()
    {
        var builder = new StringBuilder()
            .Append(Schema).Append('\0')
            .Append(ReleaseId).Append('\0')
            .Append(Environment).Append('\0')
            .Append(IntendedLineageId).Append('\0')
            .Append(State.ToString()).Append('\0')
            .Append(LineageId ?? string.Empty).Append('\0')
            .Append(CommitDetail ?? string.Empty).Append('\0')
            .Append(StagedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append('\0')
            .Append(CommittedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty).Append('\0')
            .Append(DeploymentId ?? string.Empty).Append('\0')
            .Append(Attempt.ToString(CultureInfo.InvariantCulture)).Append('\0');

        foreach (var key in Facts.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            builder.Append(key).Append('\0')
                .Append(JsonSerializer.Serialize(Facts[key])).Append('\0');
        }

        return builder.ToString();
    }

    public ArtifactDigest ComputeDigest()
        => ArtifactDigest.Compute(Encoding.UTF8.GetBytes(CanonicalForm()));
}
