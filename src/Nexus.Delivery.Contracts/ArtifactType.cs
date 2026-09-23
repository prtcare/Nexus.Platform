using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// What kind of thing an artifact is. Drives which packager produces it and what
/// <see cref="IArtifactPackager"/> is expected to guarantee about it.
///
/// <para>
/// Deliberately narrow. Each member corresponds to a packaging mechanism that exists or is planned in
/// this estate; a member with no packager is an aspiration recorded as an enum value, which is the kind
/// of thing that reads as support without being support.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtifactType
{
    /// <summary>A published .NET application or service — the output of <c>dotnet publish</c>.</summary>
    DotnetApplication,

    /// <summary>A framework-dependent library assembly set. Not an application; no entry point.</summary>
    DotnetLibrary,

    /// <summary>A static client bundle (the built output of a frontend build). Configuration must be runtime, not baked — see <see cref="ClientRuntimeConfiguration"/>.</summary>
    StaticClientBundle,

    /// <summary>A NuGet package.</summary>
    Package,

    /// <summary>An OCI image. Recorded for completeness; the W9 packagers do not yet produce one.</summary>
    ContainerImage
}
