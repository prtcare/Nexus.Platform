using System.Text;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// One member that is deliberately outside the projection, with the reason it is. A bare name would be a list
/// to be got around; the reason is what a future change has to argue with.
/// </summary>
public sealed record ReleaseDigestField(string Name, string Note);

/// <summary>
/// <b><c>release-digest-v1</c>: the named, frozen definition of which bytes a release digest is taken over.</b>
///
/// <para>
/// <b>Why this type exists at all.</b> W9.4's release-digest finding was an <i>instrumentation</i>
/// false reading, not tampering: the registered release <c>rel-2ec4c364727bcb74</c> is valid and unchanged.
/// The instrument that produced the false reading was a projection that nobody had written down — the digest
/// was computed by <see cref="ReleaseRecord.ComputeRecordDigest"/>, whose field set, order, encodings and
/// exclusions were properties of a method body rather than of a named contract. Anyone rebuilding a
/// projection from the JSON readback, or reading a digest off a transcript, was reconstructing a definition
/// instead of consulting one. This type is that definition, in one place, with tests that fail if the bytes
/// move.
/// </para>
///
/// <para>
/// <b>This is a freeze, not a change.</b> <see cref="CanonicalProjection"/> is
/// <see cref="ReleaseRecord.CanonicalForm"/> and nothing else: the projection bytes are byte-identical to
/// those the registered digest was taken over. A test asserts that this contract's hash and the record's own
/// hash agree, so the two cannot drift into two definitions of one release identity.
/// </para>
///
/// <para>
/// <b>The version identifier is a name, deliberately not a field.</b> <c>release-digest-v1</c> identifies
/// this field set. It is NOT prefixed into the projection, because the already-registered release was
/// digested over exactly these fields; inserting a marker would change its digest and therefore rewrite an
/// accepted release identity. A future version that changes the field set is a NEW contract identity with a
/// NEW digest for new releases — it does not silently become v1, and it does not retroactively re-digest
/// anything.
/// </para>
///
/// <para>
/// <b>SHA-256 over UTF-8, rendered <c>sha256:&lt;64 lowercase hex&gt;</c>.</b> The bytes hashed are the UTF-8
/// encoding of the projection of NUL-terminated fields — see <see cref="FieldTerminator"/>. No BOM, no
/// trailing terminator beyond the last field's own, no line endings anywhere in the construction.
/// </para>
/// </summary>
public static class ReleaseDigestContract
{
    /// <summary>The contract identity. See the type remarks for why this is not a field of the projection.</summary>
    public const string Version = "release-digest-v1";

    /// <summary>The leading field of every projection, so a projection is self-describing.</summary>
    public const string Marker = "nexus-release-record";

    /// <summary>The only algorithm this contract computes. Recorded on the digest itself, not assumed.</summary>
    public const string HashAlgorithm = ArtifactDigest.Sha256Algorithm;

    /// <summary>
    /// The field separator. Every field is followed by it, so no two field sequences can concatenate into the
    /// same string: <c>["ab","c"]</c> and <c>["a","bc"]</c> are different projects of bytes.
    /// </summary>
    public const char FieldTerminator = '\0';

    /// <summary>
    /// The immutable fields, in the order they are appended. <b>Order is part of the contract</b>: appending a
    /// field in a different place is a different projection, and a test asserts this declaration matches the
    /// actual byte layout rather than trusting it. Nested members are listed in
    /// <see cref="IncludedMemberNotes"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> IncludedFields =
    [
        "marker",
        "schemaVersion",
        "identity",
        "bundleId",
        "evidence",
        "dependencyLock",
        "configurationSchemaVersion",
        "health.readinessPath",
        "health.livenessPath",
        "contractVersions[]",
        "migrations",
        "rollback",
        "configurationKeys[]",
        "secretReferences[]",
        "originatingWork"
    ];

    /// <summary>
    /// The nested members of each <see cref="IncludedFields"/> entry, in append order. Present so that
    /// "every immutable field is covered" is readable rather than inferred, and so a mutation test can name
    /// each one.
    /// </summary>
    public static readonly IReadOnlyList<string> IncludedMemberNotes =
    [
        "identity: unitId, version, buildId, releaseRefName, sourceCommits[], artifacts[]",
        "identity.artifacts[]: artifactId, contentDigest, sizeBytes",
        "identity EXCLUDES inputDigest and releaseId — both are DERIVED from this projection, so projecting them would be projecting a hash into its own input",
        "evidence: buildManifestDigest, buildManifestReference, testSuiteName, testVerdict, testsTotal, testsPassed, secretScanVerdict, reproducibilityVerdict, buildsCompared, builderRunId, secretScanSubjectLabels[]",
        "dependencyLock: locked[<lock file paths, in carried order>] or unlocked(<note>)",
        "contractVersions[]: contractName, version, sourceRepository",
        "migrations: state, metadata.provider, metadata.setDigest, fromVersion, toVersion, compatibility, backupRequired, reversibility, basis",
        "rollback: state, previousReleaseId, crossesMigrationBoundary, rehearsed, basis",
        "originatingWork: workReference, title, series"
    ];

    /// <summary>
    /// Operational and lifecycle state that is <b>deliberately not projected</b>, each with the reason it is
    /// excluded. This list is the other half of the contract: a field that has no reason to be here cannot be
    /// added to <see cref="IncludedFields"/> without stating one, and a test asserts that every public member
    /// of <see cref="ReleaseRecord"/> is either projected or named here.
    /// </summary>
    public static readonly IReadOnlyList<ReleaseDigestField> ExcludedFields =
    [
        new(nameof(ReleaseRecord.CreatedAt),
            "When a record was written, not what the record says. Including it would make the same release assembled twice into two releases, and would turn a re-assembly of identical inputs into a re-registration rather than an idempotent no-op."),

        new("ReleaseLifecycleState",
            "Where the release is in DEV readiness, promotion and supersession. It lives on the registry entry, not on the record, so it is not merely excluded here: it is unreachable from the projection."),

        new("DeploymentEnvironmentId",
            "Environment is deployment state, per (release, environment), and the same record is promoted through DEV, TEST and PROD. Absent from the record by construction and asserted by reflection in ReleaseIdentityAndRecordTests."),

        new("PromotionState",
            "Promotion and deployment state, for the reason the environment is excluded: a release that carried it could not be the same release in two environments."),

        new("ReleaseGateEvidence",
            "C-1 rotation confirmation and C-2 reference protection are evidence about a decision to proceed, not claims about which bytes this is. W9.4's C-2 model change moved this member and did not touch any digest — which is the property this exclusion preserves."),

        new("ReleaseLineageRecord",
            "How a release is reachable from governed work, not what the release is. Lineage is appended after registration and must not re-identify it."),

        new("RegistryEntryMetadata",
            "Registration timestamps and registry bookkeeping are properties of the store, not of the release.")
    ];

    /// <summary>
    /// The exact bytes a release digest is taken over. Delegates to <see cref="ReleaseRecord.CanonicalForm"/>
    /// so that this contract cannot become a second, drifting definition of the same release identity.
    /// </summary>
    public static string CanonicalProjection(ReleaseRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.CanonicalForm();
    }

    /// <summary>
    /// The release digest under this contract: SHA-256 over the UTF-8 bytes of
    /// <see cref="CanonicalProjection"/>. Computed here rather than delegated, so this type is the definition;
    /// a test asserts it agrees with <see cref="ReleaseRecord.ComputeRecordDigest"/>.
    /// </summary>
    public static ArtifactDigest Compute(ReleaseRecord record)
        => ArtifactDigest.Compute(Encoding.UTF8.GetBytes(CanonicalProjection(record)));

    /// <summary>True when <paramref name="record"/> projects to <paramref name="expected"/>. The immutability check.</summary>
    public static bool Matches(ReleaseRecord record, ArtifactDigest expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        return Compute(record) == expected;
    }

    /// <summary>The projection's length in bytes when encoded as UTF-8. Reported so a mismatch can be located by offset.</summary>
    public static int ProjectionByteLength(ReleaseRecord record)
        => Encoding.UTF8.GetByteCount(CanonicalProjection(record));

    /// <summary>
    /// The offset of the first byte at which two records' projections differ, or -1 when they are identical.
    /// Used to prove that the declared field order is the byte order.
    /// </summary>
    public static int FirstDifferingOffset(ReleaseRecord left, ReleaseRecord right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var a = Encoding.UTF8.GetBytes(CanonicalProjection(left));
        var b = Encoding.UTF8.GetBytes(CanonicalProjection(right));

        var limit = Math.Min(a.Length, b.Length);

        for (var i = 0; i < limit; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }

        return a.Length == b.Length ? -1 : limit;
    }
}
