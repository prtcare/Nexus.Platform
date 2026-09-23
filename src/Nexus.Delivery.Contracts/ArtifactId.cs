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

        return new ArtifactId(
            $"{unitId.Value}/{SegmentFor(type)}/{name}@{version}",
            unitId,
            type,
            name,
            version);
    }

    /// <summary>
    /// Reads an id back from its wire form.
    ///
    /// <para>
    /// <b>This is the counterpart of <see cref="For"/>, and it lives here so that there is one of it.</b>
    /// The W9.2 store parsed this shape in two separate methods, which meant the type-segment mapping existed
    /// as two switches that had to be kept in step by hand — the same defect class that made the W9.2 secret
    /// scanner report an ellipsis as a credential, where one judgement implemented twice drifted apart. A
    /// third reader is what this method exists to prevent.
    /// </para>
    /// </summary>
    public static ArtifactId Parse(string value)
        => TryParse(value, out var artifactId)
            ? artifactId!
            : throw new ArgumentException(
                "Not an artifact id (expected '<unitId>/<type>/<name>@<version>', e.g. "
                + "'marketsurvey.api/dotnet-app/marketsurvey.api@0.1.0').",
                nameof(value));

    /// <summary>Parses without throwing, so a stored id that no longer parses can be skipped rather than aborting a read.</summary>
    public static bool TryParse(string? value, out ArtifactId? artifactId)
    {
        artifactId = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var firstSeparator = value.IndexOf('/', StringComparison.Ordinal);
        if (firstSeparator <= 0)
        {
            return false;
        }

        var remainder = value[(firstSeparator + 1)..];
        var secondSeparator = remainder.IndexOf('/', StringComparison.Ordinal);
        if (secondSeparator <= 0)
        {
            return false;
        }

        var typeSegment = remainder[..secondSeparator];
        var nameAndVersion = remainder[(secondSeparator + 1)..];

        var at = nameAndVersion.LastIndexOf('@');
        if (at <= 0 || at == nameAndVersion.Length - 1)
        {
            return false;
        }

        if (!TryTypeForSegment(typeSegment, out var type))
        {
            return false;
        }

        try
        {
            artifactId = For(
                DeploymentUnitId.Parse(value[..firstSeparator]),
                type,
                nameAndVersion[..at],
                nameAndVersion[(at + 1)..]);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Canonical wire segment for an artifact type. The one switch, shared by <see cref="For"/> and every reader.</summary>
    public static string SegmentFor(ArtifactType type) => type switch
    {
        ArtifactType.DotnetApplication => "dotnet-app",
        ArtifactType.DotnetLibrary => "dotnet-lib",
        ArtifactType.StaticClientBundle => "client-bundle",
        ArtifactType.Package => "package",
        ArtifactType.ContainerImage => "container-image",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped artifact type; add its id segment beside its packager.")
    };

    /// <summary>
    /// The inverse of <see cref="SegmentFor"/>, for callers that read an id off disk and want a typed answer
    /// rather than an exception. Returns false for a segment this contract does not know, so an id written by
    /// a future version is skipped instead of failing a whole read.
    /// </summary>
    public static bool TryTypeForSegment(string? segment, out ArtifactType type)
    {
        switch (segment)
        {
            case "dotnet-app": type = ArtifactType.DotnetApplication; return true;
            case "dotnet-lib": type = ArtifactType.DotnetLibrary; return true;
            case "client-bundle": type = ArtifactType.StaticClientBundle; return true;
            case "package": type = ArtifactType.Package; return true;
            case "container-image": type = ArtifactType.ContainerImage; return true;
            default: type = default; return false;
        }
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
