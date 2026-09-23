namespace Nexus.Delivery.Contracts;

/// <summary>
/// Who produced a bundle, and with what.
///
/// <para>
/// The SDK version is recorded because the estate already pins it to a single value via
/// <c>global.json</c> (10.0.302) and reads it from that file in CI. Recording it in the bundle
/// keeps the one piece of the build that was already deterministic traceable to the artifact it
/// produced, instead of leaving it as ambient state that the next machine may not share.
/// </para>
/// </summary>
public sealed record BuilderIdentity
{
    public BuilderIdentity(string sdkVersion, string pipelineRunId, string hostOperatingSystem, string? builderImage = null)
    {
        if (string.IsNullOrWhiteSpace(sdkVersion))
        {
            throw new ArgumentException("A builder identity must record the SDK version.", nameof(sdkVersion));
        }

        if (string.IsNullOrWhiteSpace(pipelineRunId))
        {
            throw new ArgumentException("A builder identity must be attributable to a run.", nameof(pipelineRunId));
        }

        if (string.IsNullOrWhiteSpace(hostOperatingSystem))
        {
            throw new ArgumentException("A builder identity must record the host operating system.", nameof(hostOperatingSystem));
        }

        SdkVersion = sdkVersion;
        PipelineRunId = pipelineRunId;
        HostOperatingSystem = hostOperatingSystem;
        BuilderImage = builderImage;
    }

    public string SdkVersion { get; }

    public string PipelineRunId { get; }

    public string HostOperatingSystem { get; }

    /// <summary>Container image the build ran in, when the build was containerised. Null for host builds.</summary>
    public string? BuilderImage { get; }
}
