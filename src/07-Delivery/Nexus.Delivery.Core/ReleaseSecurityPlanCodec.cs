using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The storage codec for <see cref="ReleaseSecurityPlan"/>.
///
/// <para>
/// <b>Strict on the way in, deterministic on the way out.</b> A plan is the authority controlling whether a
/// release may be deployed, so a decoder that ignored a member it did not recognise would let a writer add a
/// field that nothing reads and silently believe it had been recorded. Unknown members are refused, and the
/// refusal names them.
/// </para>
///
/// <para>
/// <b>The digest is over the canonical projection, not over these bytes.</b> Same finding as W9.3's
/// <c>D-2</c>, where the registry enforced a digest over one rendering while the tag annotation recorded
/// another, and same conclusion: a plan's identity is what it <i>means</i>. <see cref="Encode"/> exists to
/// get the record onto disk; <see cref="ReleaseSecurityPlan.CanonicalForm"/> is what the digests in the
/// application ledger are taken over.
/// </para>
///
/// <para>
/// <b>Whether an absent member means "unrecorded" is per-member and deliberate.</b> An absent
/// <c>credentialRotationStatus</c> decodes to <see cref="CredentialRotationStatus.Unknown"/>, which is never
/// a pass; an absent <c>releaseReferenceProtectionMode</c> decodes to
/// <see cref="ReleaseReferenceProtectionMode.None"/>, which refuses. Both omission paths are fail-closed, so
/// a truncated record cannot read as a permissive one.
/// </para>
/// </summary>
public static class ReleaseSecurityPlanCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Encode(ReleaseSecurityPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(PlanDto.From(plan), Options));
    }

    /// <summary>
    /// Decodes a plan, refusing anything the contract does not name. Never throws for a malformed record:
    /// a corrupt authority is a state a caller must be able to report, not an exception it must catch.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> content, out ReleaseSecurityPlan? plan, out string detail)
    {
        plan = null;
        detail = string.Empty;

        try
        {
            var dto = JsonSerializer.Deserialize<PlanDto>(content, ReadOptions);

            if (dto is null)
            {
                detail = "The release plan record decoded to nothing.";
                return false;
            }

            plan = dto.ToPlan();

            // The schema range is checked HERE rather than left to validation, because the store's reader
            // does not validate what it reads — it decodes and cross-checks. A record written by a newer
            // schema would therefore read as Present to a reader that cannot interpret it, and the members
            // it did not understand are exactly the ones a newer schema would have added. Refusing at the
            // decode is what makes SchemaVersion load-bearing rather than decorative: the record names its
            // contract, and a reader that is not that contract says so instead of guessing.
            if (plan.SchemaVersion < ReleaseSecurityPlan.MinSchemaVersion
                || plan.SchemaVersion > ReleaseSecurityPlan.MaxSchemaVersion)
            {
                detail = $"The record's schema version {plan.SchemaVersion} is outside "
                    + $"{ReleaseSecurityPlan.MinSchemaVersion}-{ReleaseSecurityPlan.MaxSchemaVersion}, so it is not a "
                    + $"'{ReleaseSecurityPlan.CanonicalMarker}' record this build can interpret. A newer record read by an older "
                    + "reader must refuse, not decode into whatever members happened to match.";

                plan = null;
                return false;
            }

            detail = $"Decoded the plan for {plan.ReleaseId} at version {plan.Version}.";
            return true;
        }
        catch (JsonException ex)
        {
            // The message is the message: it names the offending member and the path to it. Rewriting it
            // would lose the one thing a reader needs, and this text never contains a record's values.
            detail = $"The release plan record does not decode: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// The members this codec refuses when they appear in a record. Published so the refusal can be asserted
    /// by a test rather than described in a comment.
    /// </summary>
    public static IReadOnlyList<string> KnownMemberNames { get; } =
    [
        nameof(PlanDto.SchemaVersion),
        nameof(PlanDto.ReleaseId),
        nameof(PlanDto.CredentialRotationStatus),
        nameof(PlanDto.CredentialRotationDecisionReference),
        nameof(PlanDto.ReleaseReferenceProtectionMode),
        nameof(PlanDto.ReleaseRefServerSideProtectionVerified),
        nameof(PlanDto.CompensatingControlApproved),
        nameof(PlanDto.CompensatingControlVerified),
        nameof(PlanDto.AllowedEnvironmentScope),
        nameof(PlanDto.HumanDecisionReference),
        nameof(PlanDto.HumanDecisionAuthority),
        nameof(PlanDto.UpdatedAt),
        nameof(PlanDto.UpdatedBy),
        nameof(PlanDto.Reason),
        nameof(PlanDto.Version)
    ];

    /// <summary>
    /// The storage shape, with an explicit order. Order is part of what a reader sees; it is deliberately
    /// <i>not</i> part of the digest, which is taken over <see cref="ReleaseSecurityPlan.CanonicalForm"/>.
    /// </summary>
    internal sealed record PlanDto(
        [property: JsonPropertyOrder(0)] int SchemaVersion,
        [property: JsonPropertyOrder(1)] string? ReleaseId,
        [property: JsonPropertyOrder(2)] string CredentialRotationStatus,
        [property: JsonPropertyOrder(3)] string? CredentialRotationDecisionReference,
        [property: JsonPropertyOrder(4)] string ReleaseReferenceProtectionMode,
        [property: JsonPropertyOrder(5)] bool ReleaseRefServerSideProtectionVerified,
        [property: JsonPropertyOrder(6)] bool CompensatingControlApproved,
        [property: JsonPropertyOrder(7)] bool CompensatingControlVerified,
        [property: JsonPropertyOrder(8)] string AllowedEnvironmentScope,
        [property: JsonPropertyOrder(9)] string? HumanDecisionReference,
        [property: JsonPropertyOrder(10)] string? HumanDecisionAuthority,
        [property: JsonPropertyOrder(11)] string? UpdatedAt,
        [property: JsonPropertyOrder(12)] string UpdatedBy,
        [property: JsonPropertyOrder(13)] string Reason,
        [property: JsonPropertyOrder(14)] int Version)
    {
        internal static PlanDto From(ReleaseSecurityPlan plan) => new(
            plan.SchemaVersion,
            plan.ReleaseId?.Value,
            plan.CredentialRotationStatus.ToString(),
            plan.CredentialRotationDecisionReference,
            plan.ReleaseReferenceProtectionMode.ToString(),
            plan.ReleaseRefServerSideProtectionVerified,
            plan.CompensatingControlApproved,
            plan.CompensatingControlVerified,
            plan.AllowedEnvironmentScope.ToString(),
            plan.HumanDecisionReference,
            plan.HumanDecisionAuthority?.ToString(),
            plan.UpdatedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            plan.UpdatedBy,
            plan.Reason,
            plan.Version);

        internal ReleaseSecurityPlan ToPlan() => new()
        {
            SchemaVersion = SchemaVersion,
            ReleaseId = string.IsNullOrWhiteSpace(ReleaseId) ? null : Contracts.ReleaseId.Parse(ReleaseId),
            CredentialRotationStatus = Enum.Parse<CredentialRotationStatus>(CredentialRotationStatus),
            CredentialRotationDecisionReference = CredentialRotationDecisionReference,
            ReleaseReferenceProtectionMode = Enum.Parse<ReleaseReferenceProtectionMode>(ReleaseReferenceProtectionMode),
            ReleaseRefServerSideProtectionVerified = ReleaseRefServerSideProtectionVerified,
            CompensatingControlApproved = CompensatingControlApproved,
            CompensatingControlVerified = CompensatingControlVerified,
            AllowedEnvironmentScope = Enum.Parse<ReleaseReferenceProtectionScope>(AllowedEnvironmentScope),
            HumanDecisionReference = HumanDecisionReference,
            HumanDecisionAuthority = string.IsNullOrWhiteSpace(HumanDecisionAuthority)
                ? null
                : Enum.Parse<DeploymentAuthorityRole>(HumanDecisionAuthority),
            UpdatedAt = string.IsNullOrWhiteSpace(UpdatedAt)
                ? null
                : DateTimeOffset.Parse(UpdatedAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedBy = UpdatedBy ?? string.Empty,
            Reason = Reason ?? string.Empty,
            Version = Version
        };
    }
}
