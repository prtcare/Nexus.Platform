namespace Nexus.Delivery.Contracts;

/// <summary>
/// The three ratified environments. The Owner's ruling fixes both the set and the identifiers:
/// <c>ENV-DEV</c>, <c>ENV-TEST</c>, <c>ENV-PROD</c>, and these are the only three.
///
/// <para>
/// An environment that is not one of these three is a **worktree**, not an environment. This
/// estate held 82 worktrees and 428 uncommitted changes when W9.0 measured it, so conflating a
/// developer checkout with ENV-DEV is the most likely way the environment model fails in
/// practice — which is why the set is closed here rather than left open.
/// </para>
/// </summary>
public sealed record DeploymentEnvironmentId
{
    public const string Dev = "ENV-DEV";
    public const string Test = "ENV-TEST";
    public const string Prod = "ENV-PROD";

    private static readonly string[] Ratified = [Dev, Test, Prod];

    private DeploymentEnvironmentId(string value) => Value = value;

    public string Value { get; }

    public static DeploymentEnvironmentId DevEnv { get; } = new(Dev);

    public static DeploymentEnvironmentId TestEnv { get; } = new(Test);

    public static DeploymentEnvironmentId ProdEnv { get; } = new(Prod);

    /// <summary>All three ratified identifiers, in promotion order.</summary>
    public static IReadOnlyList<DeploymentEnvironmentId> All { get; } =
        [DevEnv, TestEnv, ProdEnv];

    public static DeploymentEnvironmentId Parse(string value)
    {
        if (!TryParse(value, out var environment))
        {
            throw new ArgumentException(
                $"'{value}' is not a ratified environment. The ratified set is exactly: {string.Join(", ", Ratified)}.",
                nameof(value));
        }

        return environment!;
    }

    public static bool TryParse(string? value, out DeploymentEnvironmentId? environment)
    {
        environment = Ratified.FirstOrDefault(r => string.Equals(r, value, StringComparison.Ordinal)) switch
        {
            Dev => DevEnv,
            Test => TestEnv,
            Prod => ProdEnv,
            _ => null
        };

        return environment is not null;
    }

    public override string ToString() => Value;
}
