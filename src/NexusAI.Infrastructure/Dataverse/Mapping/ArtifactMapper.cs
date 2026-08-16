using NexusAI.Domain.Artifact;
using NexusAI.Domain.WorkItem;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class ArtifactMapper
    : IRepositoryMapper<Artifact, ArtifactEntity>
{
    private const int DataverseCode = 121930000;
    private const int DataverseDocument = 121930001;
    private const int DataverseSchema = 121930002;
    private const int DataverseConfiguration = 121930003;
    private const int DataverseApi = 121930004;
    private const int DataverseTest = 121930005;
    private const int DataverseDiagram = 121930006;
    private const int DataverseOther = 121930007;

    public ArtifactEntity ToEntity(Artifact domain)
    {
        return new ArtifactEntity
        {
            Id = domain.Id.Value,
            WorkItemId = domain.WorkItemId.Value,
            Name = domain.Name,
            Type = ToDataverseType(domain.Type),
            Content = domain.Content,
            CreatedAt = domain.CreatedAt
        };
    }

    public Artifact ToDomain(ArtifactEntity entity)
    {
        return new Artifact(
            new ArtifactId(entity.Id),
            new WorkItemId(entity.WorkItemId),
            entity.Name,
            FromDataverseType(entity.Type),
            entity.Content,
            entity.CreatedAt);
    }

    private static int ToDataverseType(
        ArtifactType type)
    {
        return type switch
        {
            ArtifactType.Code =>
                DataverseCode,

            ArtifactType.Document =>
                DataverseDocument,

            ArtifactType.Schema =>
                DataverseSchema,

            ArtifactType.Configuration =>
                DataverseConfiguration,

            ArtifactType.Api =>
                DataverseApi,

            ArtifactType.Test =>
                DataverseTest,

            ArtifactType.Diagram =>
                DataverseDiagram,

            ArtifactType.Other =>
                DataverseOther,

            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported ArtifactType value.")
        };
    }

    private static ArtifactType FromDataverseType(
        int value)
    {
        return value switch
        {
            // 0 is DataverseContext's sentinel for a missing/unset
            // du_artifacttype attribute, not a real OptionSet code -
            // treat it as Other rather than crashing the read.
            0 =>
                ArtifactType.Other,

            DataverseCode =>
                ArtifactType.Code,

            DataverseDocument =>
                ArtifactType.Document,

            DataverseSchema =>
                ArtifactType.Schema,

            DataverseConfiguration =>
                ArtifactType.Configuration,

            DataverseApi =>
                ArtifactType.Api,

            DataverseTest =>
                ArtifactType.Test,

            DataverseDiagram =>
                ArtifactType.Diagram,

            DataverseOther =>
                ArtifactType.Other,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse ArtifactType value.")
        };
    }
}
