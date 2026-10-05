namespace Nexus.Delivery.Contracts;

/// <summary>
/// Everything the certification gate judges, gathered in one place.
///
/// <para>
/// Two members are deliberately <b>not</b> readable from the manifest, because neither is a property of
/// the artifact:
/// </para>
///
/// <list type="bullet">
/// <item><see cref="ExpectedActiveInputLabels"/> — what the scan was <i>supposed</i> to cover. The
/// manifest records what it <i>did</i> cover, and a scan that covered a subset reports clean about a
/// subset. Comparing the two is the only way to catch that, so the expectation has to come from outside
/// the thing being checked.</item>
/// <item><see cref="UnitRequiresEnvironmentSpecificRebuild"/> — whether the unit's build still compiles an
/// environment-dependent value in. W9.0 found two such units. Nothing in the artifact reveals this,
/// because the baked value looks like an ordinary value; it is a property of how the unit is built.</item>
/// </list>
/// </summary>
public sealed record CertificationEvidence
{
    public CertificationEvidence(
        BuildManifest manifest,
        IReadOnlyList<string> expectedActiveInputLabels,
        bool unitRequiresEnvironmentSpecificRebuild,
        bool buildSucceeded)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(expectedActiveInputLabels);

        // A scan cannot have been expected to cover nothing. An empty expectation would make the coverage
        // check vacuous — the guard-that-cannot-fail shape this estate keeps finding.
        if (expectedActiveInputLabels.Count == 0)
        {
            throw new ArgumentException(
                "Certification requires the set of active inputs the scan was expected to cover. Without it the coverage check cannot fail.",
                nameof(expectedActiveInputLabels));
        }

        Manifest = manifest;
        ExpectedActiveInputLabels = [.. expectedActiveInputLabels];
        UnitRequiresEnvironmentSpecificRebuild = unitRequiresEnvironmentSpecificRebuild;
        BuildSucceeded = buildSucceeded;
    }

    public BuildManifest Manifest { get; }

    public IReadOnlyList<string> ExpectedActiveInputLabels { get; }

    public bool UnitRequiresEnvironmentSpecificRebuild { get; }

    public bool BuildSucceeded { get; }
}
