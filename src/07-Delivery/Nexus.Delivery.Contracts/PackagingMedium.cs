using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// How a unit is packaged for deployment.
///
/// <para>
/// This is a required declaration rather than a discovered fact because W9.0 measured four of the
/// estate's five services with **no defined deployment medium at all** — only MarketSurvey ships a
/// Dockerfile. A promotion cannot move an artifact whose medium is undefined, so an undefined
/// medium must fail the build's own declaration check rather than surface as a surprise at deploy
/// time.
/// </para>
///
/// <para>
/// The production medium choice is the Owner's (W9.0 decision D-2, still open). This enum records
/// the answer; it does not select one.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackagingMedium
{
    /// <summary>Not declared. Refused for a <see cref="DeploymentUnitKind.Service"/>.</summary>
    Undeclared = 0,

    /// <summary>An OCI image. Provider-neutral: the registry backend is an adapter, not this value.</summary>
    Container,

    /// <summary>A platform app service (any provider).</summary>
    AppService,

    /// <summary>A process host started directly on a host.</summary>
    ProcessHost,

    /// <summary>Static files served by a web tier. The medium for client units.</summary>
    StaticBundle,

    /// <summary>Shipped as a package at build time. The medium for libraries.</summary>
    Package
}
