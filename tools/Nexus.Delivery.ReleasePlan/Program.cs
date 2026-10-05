using System.Globalization;
using System.Text;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;

namespace Nexus.Delivery.ReleasePlanTool;

/// <summary>
/// The governed release-plan writer, as a command.
///
/// <para>
/// <b>Why this exists at all.</b> W9.4's finding was not that a JSON file held the C-1 and C-2 security state;
/// it was that the file had <i>no writer</i>, so the only way to change the authority controlling a
/// deployment was to edit it by hand. A store with no operable writer would leave that true and merely move
/// the file. This command is the writer made reachable: it takes a typed request, hands it to
/// <see cref="IReleasePlanStore"/>, and prints the typed outcome — including every refusal, which is the
/// interesting output.
/// </para>
///
/// <para>
/// <b>The input is a request, not authority.</b> Nothing this command accepts becomes the record. The store
/// validates the record it would produce, refuses unknown and contradictory state, and only then writes it.
/// So an invalid request cannot produce a valid authority, and a mistyped flag produces a refusal rather than
/// a plan with one wrong member in it.
/// </para>
///
/// <para>
/// <b>Unknown flags are refused, mirroring the schema.</b> The record refuses unknown JSON members. A command
/// line that accepted <c>--compensating-approvde</c> and quietly ignored it would have exactly the defect the
/// schema refusal exists to prevent, one layer up — the operator would believe a control was recorded while
/// the store recorded nothing.
/// </para>
///
/// <para>
/// <b>No secrets pass through here.</b> Every member is a status, a scope, a decision reference or
/// provenance. There is no flag for a credential value and there must never be one: this command records
/// <i>that</i> a rotation was confirmed by the Owner, never what was rotated.
/// </para>
/// </summary>
public static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length < 1)
        {
            Usage();
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "show" => await ShowAsync(args),
                "create" => await WriteAsync(args, create: true),
                "apply" => await WriteAsync(args, create: false),
                _ => Fail($"'{args[0]}' is not a verb this writers understands.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"REFUSED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void Usage()
    {
        Console.Error.WriteLine("usage: Nexus.Delivery.ReleasePlan show   <planRoot> <releaseId>");
        Console.Error.WriteLine("       Nexus.Delivery.ReleasePlan create <planRoot> --release <id> --updated-by <who> --reason <text> [fields]");
        Console.Error.WriteLine("       Nexus.Delivery.ReleasePlan apply  <planRoot> --release <id> --expected-version <n> --updated-by <who> --reason <text> [fields]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("fields:");
        Console.Error.WriteLine("  --rotation Unknown|Required|Confirmed");
        Console.Error.WriteLine("  --rotation-decision <reference>        the Owner decision that confirmed the rotation");
        Console.Error.WriteLine("  --mode None|ServerSideProtected|GovernedCompensatingControl");
        Console.Error.WriteLine("  --server-side-verified                 only with --mode ServerSideProtected, and only if verified");
        Console.Error.WriteLine("  --compensating-approved");
        Console.Error.WriteLine("  --compensating-verified");
        Console.Error.WriteLine("  --scope None|DevTest");
        Console.Error.WriteLine("  --human-decision <reference>");
        Console.Error.WriteLine("  --authority Builder|DeliveryTeam|Owner|OnCall");
        Console.Error.WriteLine("  --updated-at <ISO-8601>                defaults to now, UTC");
    }

    // =====================================================================================================
    // show — read-only. Prints the authority and its ledger, so an operator can see what a write produced
    // without a second reader being able to disagree with the gates.
    // =====================================================================================================
    private static async Task<int> ShowAsync(string[] args)
    {
        if (args.Length != 3)
        {
            Usage();
            return 2;
        }

        var store = new FileReleasePlanStore(args[1]);
        var releaseId = ReleaseId.Parse(args[2]);
        var read = await store.ReadAsync(releaseId);

        Console.WriteLine($"root:      {Path.GetFullPath(args[1])}");
        Console.WriteLine($"release:   {read.ReleaseId}");
        Console.WriteLine($"state:     {read.State}");
        Console.WriteLine($"detail:    {read.Detail}");

        if (read.State == ReleasePlanReadState.Absent)
        {
            return 3;
        }

        if (read.State == ReleasePlanReadState.Corrupt || read.Plan is null)
        {
            // A corrupt authority is not "nothing recorded". Reported as a distinct non-zero code so a
            // caller cannot treat the two as the same outcome.
            Console.Error.WriteLine("REFUSED: the governed plan does not read back as a valid record.");
            return 1;
        }

        var plan = read.Plan;

        Console.WriteLine($"version:   {plan.Version}");
        Console.WriteLine($"digest:    {plan.ComputePlanDigest()}");
        Console.WriteLine($"schema:    {plan.SchemaVersion} ({ReleaseSecurityPlan.CanonicalMarker})");
        Console.WriteLine($"rotation:  {plan.CredentialRotationStatus}  decision={plan.CredentialRotationDecisionReference ?? "(none)"}");
        Console.WriteLine($"mechanism: {plan.ReleaseReferenceProtectionMode}  serverSideVerified={plan.ReleaseRefServerSideProtectionVerified}");
        Console.WriteLine($"control:   approved={plan.CompensatingControlApproved} verified={plan.CompensatingControlVerified} scope={plan.AllowedEnvironmentScope}");
        Console.WriteLine($"decision:  {plan.HumanDecisionReference ?? "(none)"} by {plan.HumanDecisionAuthority?.ToString() ?? "(none)"}");
        Console.WriteLine($"updated:   {plan.UpdatedAt:O} by {plan.UpdatedBy}");
        Console.WriteLine($"reason:    {plan.Reason}");

        var verdict = ReleaseSecurityPlanValidation.Validate(plan);
        Console.WriteLine($"validate:  valid={verdict.IsValid} refusals=[{string.Join(", ", verdict.RefusalReasons)}]");

        foreach (var environment in new[] { DeploymentEnvironmentId.DevEnv, DeploymentEnvironmentId.TestEnv, DeploymentEnvironmentId.ProdEnv })
        {
            var readiness = ReleaseSecurityPlanValidation.AssessReadiness(plan, environment);
            Console.WriteLine($"ready {environment.Value,-8} {readiness.IsReady}  [{string.Join(", ", readiness.RefusalReasons)}]");
        }

        Console.WriteLine();
        Console.WriteLine("applications:");
        foreach (var application in await store.ReadApplicationsAsync(releaseId))
        {
            Console.WriteLine($"  v{application.PreviousVersion}→{application.Version} {application.AppliedAt:O} {application.AppliedBy} "
                + $"mode={application.Mode} before={application.BeforeHash} after={application.AfterHash}");
            Console.WriteLine($"      reason: {application.Reason}");
        }

        return 0;
    }

    // =====================================================================================================
    // create / apply — the write path.
    // =====================================================================================================
    private static async Task<int> WriteAsync(string[] args, bool create)
    {
        if (args.Length < 2)
        {
            Usage();
            return 2;
        }

        var root = args[1];
        var fields = ParseFields(args, 2);

        var releaseIdText = Required(fields, "release");
        var updatedBy = Required(fields, "updated-by");
        var reason = Required(fields, "reason");

        Reject(fields, "release", "updated-by", "reason", "rotation", "rotation-decision", "mode",
            "compensating-approved", "compensating-verified", "scope", "human-decision", "authority",
            "server-side-verified", "updated-at", "expected-version");

        var releaseId = ReleaseId.Parse(releaseIdText);
        var updatedAt = fields.ContainsKey("updated-at")
            ? DateTimeOffset.Parse(Required(fields, "updated-at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : DateTimeOffset.UtcNow;

        var store = new FileReleasePlanStore(root);

        ReleasePlanWriteOutcome outcome;

        if (create)
        {
            if (fields.ContainsKey("expected-version"))
            {
                return Fail("'create' records a first version, so it takes no --expected-version. "
                    + "An expectation about a version that does not exist yet is not a check.");
            }

            var record = new ReleaseSecurityPlan
            {
                SchemaVersion = ReleaseSecurityPlan.CurrentSchemaVersion,
                ReleaseId = releaseId,
                CredentialRotationStatus = EnumField<CredentialRotationStatus>(fields, "rotation") ?? CredentialRotationStatus.Unknown,
                CredentialRotationDecisionReference = Optional(fields, "rotation-decision"),
                ReleaseReferenceProtectionMode = EnumField<ReleaseReferenceProtectionMode>(fields, "mode") ?? ReleaseReferenceProtectionMode.None,
                ReleaseRefServerSideProtectionVerified = fields.ContainsKey("server-side-verified"),
                CompensatingControlApproved = fields.ContainsKey("compensating-approved"),
                CompensatingControlVerified = fields.ContainsKey("compensating-verified"),
                AllowedEnvironmentScope = EnumField<ReleaseReferenceProtectionScope>(fields, "scope") ?? ReleaseReferenceProtectionScope.None,
                HumanDecisionReference = Optional(fields, "human-decision"),
                HumanDecisionAuthority = EnumField<DeploymentAuthorityRole>(fields, "authority"),
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy,
                Reason = reason,
                Version = 1
            };

            outcome = await store.CreateAsync(record);
        }
        else
        {
            var expected = int.Parse(Required(fields, "expected-version"), CultureInfo.InvariantCulture);

            var update = new ReleaseSecurityPlanUpdate
            {
                ReleaseId = releaseId,
                ExpectedVersion = expected,
                CredentialRotationStatus = EnumField<CredentialRotationStatus>(fields, "rotation"),
                CredentialRotationDecisionReference = Optional(fields, "rotation-decision"),
                ReleaseReferenceProtectionMode = EnumField<ReleaseReferenceProtectionMode>(fields, "mode"),
                ReleaseRefServerSideProtectionVerified = fields.ContainsKey("server-side-verified") ? true : null,
                CompensatingControlApproved = fields.ContainsKey("compensating-approved") ? true : null,
                CompensatingControlVerified = fields.ContainsKey("compensating-verified") ? true : null,
                AllowedEnvironmentScope = EnumField<ReleaseReferenceProtectionScope>(fields, "scope"),
                HumanDecisionReference = Optional(fields, "human-decision"),
                HumanDecisionAuthority = EnumField<DeploymentAuthorityRole>(fields, "authority"),
                UpdatedAt = updatedAt,
                UpdatedBy = updatedBy,
                Reason = reason
            };

            outcome = await store.ApplyAsync(update);
        }

        Console.WriteLine($"root:        {Path.GetFullPath(root)}");
        Console.WriteLine($"release:     {releaseId}");
        Console.WriteLine($"accepted:    {outcome.IsAccepted}");
        Console.WriteLine($"refusal:     {outcome.RefusalReason}");
        Console.WriteLine($"version:     {outcome.Version}");
        Console.WriteLine($"before hash: {outcome.BeforeHash?.ToString() ?? "(none)"}");
        Console.WriteLine($"after hash:  {outcome.AfterHash?.ToString() ?? "(none)"}");
        Console.WriteLine($"detail:      {outcome.Detail}");

        if (!outcome.IsAccepted)
        {
            foreach (var line in outcome.Verdict?.Detail ?? [])
            {
                Console.WriteLine($"             {line}");
            }

            Console.Error.WriteLine($"REFUSED [{outcome.RefusalReason}] {outcome.Detail}");
            return 1;
        }

        Console.WriteLine("RESULT: RELEASE_PLAN_RECORDED");
        return 0;
    }

    /// <summary>
    /// Parses <c>--name value</c> pairs and boolean switches into one dictionary.
    ///
    /// <para>
    /// A switch is a name with no following value. That is ambiguous with a value that happens to start with
    /// <c>--</c>, so a name followed by another name is read as a switch rather than as a value — which is why
    /// every string member this command accepts is documented as a reference or a reason, and neither begins
    /// with a hyphen.
    /// </para>
    /// </summary>
    private static Dictionary<string, string?> ParseFields(string[] args, int start)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = start; i < args.Length; i++)
        {
            var name = args[i];

            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"'{name}' is a stray argument: every input is a --name.");
            }

            name = name[2..];

            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);

            if (fields.ContainsKey(name))
            {
                throw new InvalidOperationException($"'--{name}' was given more than once. Two values for one member is not a request, it is a guess about which one is meant.");
            }

            fields[name] = hasValue ? args[++i] : null;
        }

        return fields;
    }

    private static string Required(Dictionary<string, string?> fields, string name)
        => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"--{name} is required.");

    private static string? Optional(Dictionary<string, string?> fields, string name)
        => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// Reads a typed member, distinguishing <i>absent</i> from <i>present but unparseable</i>.
    ///
    /// <para>
    /// Absent returns null and means "leave unchanged". A value that is present but not a member of the enum
    /// throws, rather than falling back to a default: silently reading <c>--scope DevTestt</c> as "no scope
    /// change" would leave an operator believing a scope had been recorded.
    /// </para>
    /// </summary>
    private static T? EnumField<T>(Dictionary<string, string?> fields, string name) where T : struct, Enum
        => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Enum.Parse<T>(value, ignoreCase: true)
            : null;

    private static void Reject(Dictionary<string, string?> fields, params string[] known)
    {
        var unknown = fields.Keys.Where(k => !known.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"unknown input(s) [{string.Join(", ", unknown)}]. The record this writer produces refuses unknown members, "
                + "and accepting an unknown flag here would be the same defect one layer up: the operator would believe a "
                + "control had been recorded while nothing was.");
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"REFUSED: {message}");
        return 2;
    }
}
