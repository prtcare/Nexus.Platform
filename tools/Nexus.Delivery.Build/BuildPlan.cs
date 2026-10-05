using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Build;

/// <summary>
/// The description of one build-and-certify run. Supplied as JSON so that the plan for a run is itself a
/// reviewable artifact, rather than a command line reconstructed from shell history.
/// </summary>
internal sealed record BuildPlan
{
    [JsonPropertyName("buildDefinitionVersion")] public string BuildDefinitionVersion { get; init; } = "build-definition-v1";
    [JsonPropertyName("unitId")] public string UnitId { get; init; } = string.Empty;
    [JsonPropertyName("artifactName")] public string ArtifactName { get; init; } = string.Empty;
    [JsonPropertyName("artifactVersion")] public string ArtifactVersion { get; init; } = "0.1.0";
    [JsonPropertyName("artifactType")] public string ArtifactType { get; init; } = "dotnet-app";
    [JsonPropertyName("buildConfiguration")] public string BuildConfiguration { get; init; } = "Release";

    /// <summary>The governed repository. The commit and cleanliness are read from here.</summary>
    [JsonPropertyName("repositoryLabel")] public string RepositoryLabel { get; init; } = string.Empty;
    [JsonPropertyName("repositoryRoot")] public string RepositoryRoot { get; init; } = string.Empty;

    /// <summary>
    /// One source root per build round. More than one — at different absolute paths — turns the
    /// reproducibility comparison into a path-independence proof, which is the stronger property and the
    /// one that matters when an artifact is built on one machine and verified on another.
    /// </summary>
    [JsonPropertyName("sourceRoots")] public string[] SourceRoots { get; init; } = [];

    /// <summary>Project file to publish, relative to each source root.</summary>
    [JsonPropertyName("projectPath")] public string ProjectPath { get; init; } = string.Empty;

    [JsonPropertyName("testCommand")] public string[] TestCommand { get; init; } = [];
    [JsonPropertyName("testSuiteName")] public string TestSuiteName { get; init; } = string.Empty;

    [JsonPropertyName("scanSubjects")] public ScanSubjectPlan[] ScanSubjects { get; init; } = [];
    [JsonPropertyName("runtimeConfigurationKeys")] public string[] RuntimeConfigurationKeys { get; init; } = [];

    /// <summary>
    /// Whether the unit still requires an environment-specific rebuild. Supplied by the plan because it is a
    /// property of how the unit is built, not of the artifact — a baked environment value looks exactly like
    /// an ordinary one, so nothing in the output can reveal it.
    /// </summary>
    [JsonPropertyName("unitRequiresEnvironmentSpecificRebuild")] public bool UnitRequiresEnvironmentSpecificRebuild { get; init; }

    /// <summary>
    /// Extra strings that, if found inside the packaged artifact, indicate an environment dependence — an
    /// endpoint literal, a tenant host, anything the plan knows should not be in the bytes. A marker found
    /// here can force a refusal; nothing here can clear a finding the inspector produces on its own.
    /// </summary>
    [JsonPropertyName("forbiddenArtifactMarkers")] public string[] ForbiddenArtifactMarkers { get; init; } = [];

    [JsonPropertyName("storeRoot")] public string StoreRoot { get; init; } = string.Empty;
    [JsonPropertyName("workingRoot")] public string WorkingRoot { get; init; } = string.Empty;
    [JsonPropertyName("evidenceDirectory")] public string EvidenceDirectory { get; init; } = string.Empty;

    /// <summary>Release-ref names to look for; empty means the policy default.</summary>
    [JsonPropertyName("releaseRefNames")] public string[] ReleaseRefNames { get; init; } = [];

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static BuildPlan Load(string path)
    {
        var plan = JsonSerializer.Deserialize<BuildPlan>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"The plan at '{path}' parsed to nothing.");

        if (string.IsNullOrWhiteSpace(plan.UnitId) || string.IsNullOrWhiteSpace(plan.ArtifactName))
        {
            throw new InvalidOperationException("A plan must name its unit and artifact.");
        }

        if (plan.SourceRoots.Length < 2)
        {
            throw new InvalidOperationException(
                "A plan needs at least two source roots. One build agreeing with itself is not a reproducibility proof.");
        }

        if (plan.ScanSubjects.Length == 0)
        {
            throw new InvalidOperationException(
                "A plan needs at least one scan subject. A build that scanned nothing is not a clean build.");
        }

        return plan;
    }

    internal ArtifactType ResolveArtifactType() => ArtifactType switch
    {
        "dotnet-app" => Contracts.ArtifactType.DotnetApplication,
        "dotnet-lib" => Contracts.ArtifactType.DotnetLibrary,
        "client-bundle" => Contracts.ArtifactType.StaticClientBundle,
        "package" => Contracts.ArtifactType.Package,
        "container-image" => Contracts.ArtifactType.ContainerImage,
        var other => throw new InvalidOperationException($"Unknown artifact type '{other}'.")
    };
}

internal sealed record ScanSubjectPlan
{
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("root")] public string Root { get; init; } = string.Empty;
    [JsonPropertyName("classification")] public string Classification { get; init; } = "ActiveBuildInput";
    [JsonPropertyName("quarantineJustification")] public string? QuarantineJustification { get; init; }

    internal ScanSubject ToSubject() => new(
        Label,
        Root,
        string.Equals(Classification, "QuarantinedHistorical", StringComparison.OrdinalIgnoreCase)
            ? ScanSubjectClass.QuarantinedHistorical
            : ScanSubjectClass.ActiveBuildInput,
        QuarantineJustification);
}
