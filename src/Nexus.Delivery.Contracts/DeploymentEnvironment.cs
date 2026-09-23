namespace Nexus.Delivery.Contracts;

/// <summary>
/// A ratified environment's declaration.
///
/// <para>
/// <b>Ratified, not discovered.</b> W9.0 measured that no environment definition existed anywhere in
/// the estate — <c>CONFIGURATION_STANDARDS.md</c> §16 said so in the estate's own words, and no
/// <c>appsettings.Test.json</c> or <c>appsettings.Production.json</c> existed in any repository. The
/// three environments are therefore given, by the Owner, and this record describes one.
/// </para>
///
/// <para>
/// <b>What may vary is a property of the environment, not a convention.</b> Per the build-once rule,
/// DEV/TEST/PROD may differ in configuration, secret references, endpoints, infrastructure
/// parameters and scaling. They may not differ in application code. <see cref="PromotionOrder"/>
/// exists so that "PROD is promoted from TEST, never from DEV" (rule R-4) is a comparison rather
/// than a sentence nobody checks.
/// </para>
/// </summary>
public sealed record DeploymentEnvironment
{
    public DeploymentEnvironment(
        DeploymentEnvironmentId environmentId,
        string purpose,
        int promotionOrder,
        bool requiresOwnerAuthorizationToEnter,
        bool holdsRealData)
    {
        ArgumentNullException.ThrowIfNull(environmentId);

        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("An environment must state its purpose.", nameof(purpose));
        }

        if (promotionOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(promotionOrder), promotionOrder, "Promotion order is zero-based and cannot be negative.");
        }

        EnvironmentId = environmentId;
        Purpose = purpose;
        PromotionOrder = promotionOrder;
        RequiresOwnerAuthorizationToEnter = requiresOwnerAuthorizationToEnter;
        HoldsRealData = holdsRealData;
    }

    public DeploymentEnvironmentId EnvironmentId { get; }

    public string Purpose { get; }

    public int PromotionOrder { get; }

    public bool RequiresOwnerAuthorizationToEnter { get; }

    public bool HoldsRealData { get; }

    /// <summary>
    /// The three ratified environments. Order is promotion order; <c>ENV-PROD</c> requires an Owner
    /// authorization to enter, and is the only one holding real data (rule R-1: no data flows downward).
    /// </summary>
    public static IReadOnlyList<DeploymentEnvironment> Ratified { get; } =
    [
        new(DeploymentEnvironmentId.DevEnv, "Integration of promoted artifacts; operator verification.", 0, false, false),
        new(DeploymentEnvironmentId.TestEnv, "Acceptance of an artifact already proven in ENV-DEV.", 1, false, false),
        new(DeploymentEnvironmentId.ProdEnv, "Live service.", 2, true, true)
    ];

    public static DeploymentEnvironment For(DeploymentEnvironmentId environmentId)
        => Ratified.FirstOrDefault(e => e.EnvironmentId == environmentId)
           ?? throw new ArgumentException($"'{environmentId}' is not a ratified environment.", nameof(environmentId));

    /// <summary>Rule R-4: an artifact reaches this environment only from the one immediately before it.</summary>
    public DeploymentEnvironmentId? PromotedFrom =>
        PromotionOrder == 0 ? null : Ratified[PromotionOrder - 1].EnvironmentId;
}
