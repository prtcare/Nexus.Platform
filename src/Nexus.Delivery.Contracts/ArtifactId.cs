namespace Nexus.Delivery.Contracts;

/// <summary>
/// The immutable coordinate of one built artifact: <c>&lt;unitId&gt;/&lt;type&gt;/&lt;name&gt;@&lt;version&gt;</c>.
///
/// <para>
/// <b>The version is part of the id, and that is what makes the store's immutability rule enforceable.</b>
/// The registry requirement is "existing ArtifactId + different bytes → REFUSE". That rule is only
/// meaningful if an id can persist across content changes — so an id must name a *release coordinate*,
/// not a name. If the id were just a name, the rule would forbid ever rebuilding, and if it were just a
/// content hash the rule could never fire. Naming the version inside the id is what makes both the rule
/// and the workflow true at once: the same version may never be re-published with different content, and
/// a new build carries a new version and therefore a new id.
/// </para>
///
/// <para>
/// Example: <c>marketsurvey.api/dotnet-app/marketsurvey.api@0.1.0</c>
/// </para>
/// </summary>
public sealed record ArtifactId
{
    private ArtifactId(string value, DeploymentUnitId unitId, ArtifactType type, string name, string version)
    {
        Value = value;
        UnitId = unitId;
        Type = type;
        Name = name;
        Version = version;
    }

    public string Value { get; }

    public DeploymentUnitId UnitId { get; }

    public ArtifactType Type { get; }

    /// <summary>The artifact's name within its unit, e.g. <c>marketsurvey.api</c> or <c>web</c>.</summary>
    public string Name { get; }

    /// <summary>The release version. Part of the id, so republishing a version with new bytes is refused.</summary>
    public string Version { get; }

    public static ArtifactId For(DeploymentUnitId unitId, ArtifactType type, string name, string version)
    {
        ArgumentNullException.ThrowIfNull(unitId);

        if (!CredentialShape.IsNameShaped(name))
        {
            throw new ArgumentException("An artifact name must be name-shaped.", nameof(name));
        }

        if (!IsValidVersion(version))
        {
            throw new ArgumentException(
                "An artifact version must be dotted segments of digits, optionally with a name-shaped suffix "
                + "(e.g. '0.1.0', '1.2.3-rc1'). It may not contain a separator that would make the id ambiguous.",
                nameof(version));
        }

        var typeSegment = type switch
        {
            ArtifactType.DotnetApplication => "dotnet-app",
            ArtifactType.DotnetLibrary => "dotnet-lib",
            ArtifactType.StaticClientBundle => "client-bundle",
            ArtifactType.Package => "package",
            ArtifactType.ContainerImage => "container-image",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped artifact type; add its id segment beside its packager.")
        };

        return new ArtifactId(
            $"{unitId.Value}/{typeSegment}/{name}@{version}",
            unitId,
            type,
            name,
            version);
    }

    public static bool IsValidVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var core = version;
        var plus = version.IndexOf('+');
        if (plus >= 0)
        {
            // Build metadata after '+' is part of the version and must not smuggle a path separator.
            core = version[..plus];
        }

        var dash = core.IndexOf('-');
        var numeric = dash < 0 ? core : core[..dash];
        var suffix = dash < 0 ? null : core[(dash + 1)..];

        if (numeric.Length == 0 || !numeric.Split('.').All(part => part.Length > 0 && part.All(char.IsAsciiDigit)))
        {
            return false;
        }

        return suffix is null || CredentialShape.IsNameShaped(suffix);
    }

    public override string ToString() => Value;
}
