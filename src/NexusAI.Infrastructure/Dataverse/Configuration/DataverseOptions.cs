namespace NexusAI.Infrastructure.Dataverse.Configuration;

public sealed class DataverseOptions
{
    public const string SectionName = "Dataverse";

    public string Url { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}