using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Knowledge;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class KnowledgeMapper
    : IRepositoryMapper<Knowledge, KnowledgeEntity>
{
    // KnowledgeType
    private const int DataverseDocumentation = 121930000;
    private const int DataverseTechnicalKnowledge = 121930001;
    private const int DataverseLessonLearned = 121930002;
    private const int DataverseSpecification = 121930003;
    private const int DataverseGuideline = 121930004;
    private const int DataverseReference = 121930005;
    private const int DataverseExtractedKnowledge = 121930006;

    // KnowledgeStatus
    private const int DataverseDraft = 121930000;
    private const int DataverseActive = 121930001;
    private const int DataverseArchived = 121930002;
    private const int DataverseSuperseded = 121930003;

    public KnowledgeEntity ToEntity(Knowledge domain)
    {
        return new KnowledgeEntity
        {
            Id = domain.Id.Value,
            WorkspaceId = domain.WorkspaceId.Value,
            Title = domain.Title,
            Content = domain.Content,
            Type = ToDataverseType(domain.Type),
            Status = ToDataverseStatus(domain.Status),
            CreatedAt = domain.CreatedAt
        };
    }

    public Knowledge ToDomain(KnowledgeEntity entity)
    {
        var knowledge = new Knowledge(
            new KnowledgeId(entity.Id),
            new WorkspaceId(entity.WorkspaceId),
            entity.Title,
            entity.Content,
            FromDataverseType(entity.Type),
            entity.CreatedAt);

        knowledge.ChangeStatus(
            FromDataverseStatus(entity.Status));

        return knowledge;
    }

    private static int ToDataverseType(
        KnowledgeType type)
    {
        return type switch
        {
            KnowledgeType.Documentation =>
                DataverseDocumentation,

            KnowledgeType.TechnicalKnowledge =>
                DataverseTechnicalKnowledge,

            KnowledgeType.LessonLearned =>
                DataverseLessonLearned,

            KnowledgeType.Specification =>
                DataverseSpecification,

            KnowledgeType.Guideline =>
                DataverseGuideline,

            KnowledgeType.Reference =>
                DataverseReference,

            KnowledgeType.ExtractedKnowledge =>
                DataverseExtractedKnowledge,

            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported KnowledgeType.")
        };
    }

    private static KnowledgeType FromDataverseType(
        int value)
    {
        return value switch
        {
            DataverseDocumentation =>
                KnowledgeType.Documentation,

            DataverseTechnicalKnowledge =>
                KnowledgeType.TechnicalKnowledge,

            DataverseLessonLearned =>
                KnowledgeType.LessonLearned,

            DataverseSpecification =>
                KnowledgeType.Specification,

            DataverseGuideline =>
                KnowledgeType.Guideline,

            DataverseReference =>
                KnowledgeType.Reference,

            DataverseExtractedKnowledge =>
                KnowledgeType.ExtractedKnowledge,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse KnowledgeType value.")
        };
    }

    private static int ToDataverseStatus(
        KnowledgeStatus status)
    {
        return status switch
        {
            KnowledgeStatus.Draft =>
                DataverseDraft,

            KnowledgeStatus.Active =>
                DataverseActive,

            KnowledgeStatus.Archived =>
                DataverseArchived,

            KnowledgeStatus.Superseded =>
                DataverseSuperseded,

            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported KnowledgeStatus.")
        };
    }

    private static KnowledgeStatus FromDataverseStatus(
        int value)
    {
        return value switch
        {
            DataverseDraft =>
                KnowledgeStatus.Draft,

            DataverseActive =>
                KnowledgeStatus.Active,

            DataverseArchived =>
                KnowledgeStatus.Archived,

            DataverseSuperseded =>
                KnowledgeStatus.Superseded,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse KnowledgeStatus value.")
        };
    }
}