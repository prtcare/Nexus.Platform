namespace Nexus.Delivery.Contracts;

/// <summary>
/// The toolchain that produced an artifact.
///
/// <para>
/// <b>Why the runtime version is separate from the SDK version.</b> This estate pins the SDK in
/// <c>global.json</c> and reads it from that file; W9.1 measured that the pin is <c>10.0.302</c> with
/// <c>rollForward: latestFeature</c>, so the machine that built resolves <c>10.0.400</c>. Both facts
/// belong in the record — the pin is the *policy* and the resolved version is the *fact*, and a bundle
/// that records only the policy cannot be rebuilt on a machine whose feed has moved.
/// </para>
///
/// <para>
/// <see cref="ContainerImage"/> is nullable and provider-neutral: it names the build environment when the
/// build ran in a container, whatever the registry. Nothing here names a cloud.
/// </para>
/// </summary>
public sealed record ToolchainIdentity
{
    public ToolchainIdentity(
        string sdkVersion,
        string runtimeVersion,
        string hostOperatingSystem,
        string? containerImage = null,
        string? nodeVersion = null)
    {
        if (string.IsNullOrWhiteSpace(sdkVersion))
        {
            throw new ArgumentException("A toolchain identity must record the SDK version.", nameof(sdkVersion));
        }

        if (string.IsNullOrWhiteSpace(runtimeVersion))
        {
            throw new ArgumentException("A toolchain identity must record the runtime version.", nameof(runtimeVersion));
        }

        if (string.IsNullOrWhiteSpace(hostOperatingSystem))
        {
            throw new ArgumentException("A toolchain identity must record the host operating system.", nameof(hostOperatingSystem));
        }

        SdkVersion = sdkVersion;
        RuntimeVersion = runtimeVersion;
        HostOperatingSystem = hostOperatingSystem;
        ContainerImage = containerImage;
        NodeVersion = nodeVersion;
    }

    public string SdkVersion { get; }

    public string RuntimeVersion { get; }

    /// <summary>e.g. <c>win-x64</c>, <c>linux-x64</c>. Recorded because it changes the output bytes.</summary>
    public string HostOperatingSystem { get; }

    public string? ContainerImage { get; }

    /// <summary>Recorded only for units whose build invokes a JavaScript toolchain.</summary>
    public string? NodeVersion { get; }

    public override string ToString()
        => $"sdk={SdkVersion};runtime={RuntimeVersion};os={HostOperatingSystem}"
           + (ContainerImage is null ? string.Empty : $";image={ContainerImage}")
           + (NodeVersion is null ? string.Empty : $";node={NodeVersion}");
}
