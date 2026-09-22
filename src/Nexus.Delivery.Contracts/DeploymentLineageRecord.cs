namespace Nexus.Delivery.Contracts;

/// <summary>
/// An append-only record of one deployment act.
///
/// <para>
/// <b>Invariant L-1, enforced by construction:</b> a lineage record contains <b>key names, never
/// values.</b> Configuration appears as <see cref="ConfigurationKeys"/>; secrets appear as
/// <see cref="SecretReferences"/>, whose type refuses a credential-shaped string. There is no member
/// on this record that can hold a value, because a lineage record is the most likely place a value
/// would leak — it is a convenient place to "record what we deployed with", and the estate's own
/// standards already warn that the most common invalid configuration value is a malformed secret
/// (<c>CONFIGURATION_STANDARDS.md</c> §11).
/// </para>
///
/// <para>
/// <b>What a record proves.</b> An intent and an action — that the transition's gates passed, which
/// includes the digest-in-target check. It does <b>not</b> prove runtime consumption: that the running
/// process actually loaded these bytes. W8F's independent verification of W8D made this distinction
/// the finding — the cutover was <i>confirmed</i> while the runtime-consumption claims were <i>not</i>
/// — so the limitation is named here (L-8) rather than left to be inferred, and
/// <see cref="RuntimeConsumptionObserved"/> is how a future verifier records that it closed.
/// </para>
///
/// <para>
/// The record extends the estate's existing governed ledger rather than creating a second one;
/// <c>L-W9-*</c> continues the series recorded in the authority workbook's <c>13_GitLineage</c> sheet.
/// </para>
/// </summary>
public sealed record DeploymentLineageRecord
{
    public const string SeriesPrefix = "L-W9-";

    public DeploymentLineageRecord(
        string lineageId,
        DateTimeOffset occurredAt,
        DeploymentTransition transition,
        BundleId bundleId,
        PromotionState fromState,
        PromotionState toState,
        DeploymentEnvironmentId environment,
        string sourceCommitSha,
        IReadOnlyList<ReleaseArtifact> artifacts,
        MigrationMetadata? migrations = null,
        DeploymentAuthorization? authorization = null,
        string? reason = null,
        BundleId? previousBundleId = null,
        IReadOnlyList<string>? configurationKeys = null,
        IReadOnlyList<SecretReference>? secretReferences = null,
        bool? runtimeConsumptionObserved = null)
    {
        if (!IsValidLineageId(lineageId))
        {
            throw new ArgumentException(
                $"A lineage id is '{SeriesPrefix}<digits>' (continuing the estate's L-series). Received '{lineageId}'.",
                nameof(lineageId));
        }

        ArgumentNullException.ThrowIfNull(bundleId);
        ArgumentNullException.ThrowIfNull(environment);

        if (string.IsNullOrWhiteSpace(sourceCommitSha) || !IsHex(sourceCommitSha))
        {
            throw new ArgumentException("A lineage record must name the source commit (hexadecimal).", nameof(sourceCommitSha));
        }

        if (artifacts is null || artifacts.Count == 0)
        {
            throw new ArgumentException("A lineage record must name the artifacts it moved.", nameof(artifacts));
        }

        // L-5: a rollback or a quarantine is a decision, not an observation. Both must say who and why.
        if (transition is DeploymentTransition.Rollback or DeploymentTransition.Quarantine)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    $"A {transition} record must carry a reason. An unexplained {transition} is indistinguishable from a failure.",
                    nameof(reason));
            }

            if (authorization is null)
            {
                throw new ArgumentException($"A {transition} record must name the authorising actor.", nameof(authorization));
            }
        }

        if (transition == DeploymentTransition.PromoteToProd && authorization?.IsOwnerAuthorized != true)
        {
            throw new ArgumentException(
                "A promotion into ENV-PROD must carry an Owner authorization. Owner-reserved means reserved.",
                nameof(authorization));
        }

        foreach (var key in configurationKeys ?? [])
        {
            if (!CredentialShape.IsNameShaped(key))
            {
                throw new ArgumentException(
                    $"A configuration KEY must be name-shaped; '{Redact(key)}' is not. Invariant L-1 forbids a value in a lineage record.",
                    nameof(configurationKeys));
            }
        }

        if (reason is not null && CredentialShape.LooksLikeCredentialValue(reason))
        {
            throw new ArgumentException(
                "A free-text reason must not contain a credential-shaped value. Invariant L-1.",
                nameof(reason));
        }

        LineageId = lineageId;
        OccurredAt = occurredAt;
        Transition = transition;
        BundleId = bundleId;
        FromState = fromState;
        ToState = toState;
        Environment = environment;
        SourceCommitSha = sourceCommitSha;
        Artifacts = [.. artifacts];
        Migrations = migrations ?? MigrationMetadata.None;
        Authorization = authorization;
        Reason = reason;
        PreviousBundleId = previousBundleId;
        ConfigurationKeys = [.. configurationKeys ?? []];
        SecretReferences = [.. secretReferences ?? []];
        RuntimeConsumptionObserved = runtimeConsumptionObserved;
    }

    public string LineageId { get; }

    public DateTimeOffset OccurredAt { get; }

    public DeploymentTransition Transition { get; }

    public BundleId BundleId { get; }

    public PromotionState FromState { get; }

    public PromotionState ToState { get; }

    public DeploymentEnvironmentId Environment { get; }

    public string SourceCommitSha { get; }

    public IReadOnlyList<ReleaseArtifact> Artifacts { get; }

    public MigrationMetadata Migrations { get; }

    public DeploymentAuthorization? Authorization { get; }

    public string? Reason { get; }

    /// <summary>Required for a rollback; the bundle the environment was returned to.</summary>
    public BundleId? PreviousBundleId { get; }

    /// <summary>Key NAMES only.</summary>
    public IReadOnlyList<string> ConfigurationKeys { get; }

    /// <summary>Reference NAMES only — the type makes a value unrepresentable.</summary>
    public IReadOnlyList<SecretReference> SecretReferences { get; }

    /// <summary>
    /// L-8: true only when something observed the running process serving this bundle. Null means
    /// "not observed", which is the honest value today — no unit in the estate reports its own build
    /// id (W9.0 F-7.1), so no verifier can currently assert this.
    /// </summary>
    public bool? RuntimeConsumptionObserved { get; }

    /// <summary>True when a human-readable actor string was replaced because it was credential-shaped.</summary>
    private static string Redact(string? value)
        => string.IsNullOrEmpty(value) ? "(empty)" : $"(credential-shaped, {value.Length} chars, value withheld)";

    private static bool IsValidLineageId(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.StartsWith(SeriesPrefix, StringComparison.Ordinal)
           && value.Length > SeriesPrefix.Length
           && value[SeriesPrefix.Length..].All(char.IsAsciiDigit);

    private static bool IsHex(string value)
        => value.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
}
