using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Project;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class ConversationMapper
    : IRepositoryMapper<Conversation, ConversationEntity>
{
    private const int DataverseStatusActive = 121930000;
    private const int DataverseStatusReview = 121930001;
    private const int DataverseStatusFinalized = 121930002;
    private const int DataverseStatusArchived = 121930003;

    private const int DataverseTypeStandalone = 121930000;
    private const int DataverseTypeMain = 121930001;
    private const int DataverseTypeSubChat = 121930002;
    private const int DataverseTypeWorkspace = 121930003;

    private const int DataverseVisibilityPrivate = 121930000;
    private const int DataverseVisibilityWorkspace = 121930001;
    private const int DataverseVisibilityProject = 121930002;
    private const int DataverseVisibilityShared = 121930003;

    public ConversationEntity ToEntity(Conversation domain)
    {
        return new ConversationEntity
        {
            Id = domain.Id.Value,
            ProjectId = domain.ProjectId.Value,
            WorkspaceId = domain.WorkspaceId.Value,
            Title = domain.Title,
            Description = domain.Description,
            Type = ToDataverseType(domain.Type),
            Visibility = ToDataverseVisibility(domain.Visibility),
            Status = ToDataverseStatus(domain.Status),
            CreatedAt = domain.CreatedAt,
            LastMessageOn = domain.LastMessageOn
        };
    }

    public Conversation ToDomain(ConversationEntity entity)
    {
        var conversation = new Conversation(
            new ConversationId(entity.Id),
            new ProjectId(entity.ProjectId),
            new WorkspaceId(entity.WorkspaceId),
            entity.Title,
            entity.Description ?? string.Empty,
            FromDataverseType(entity.Type),
            FromDataverseVisibility(entity.Visibility),
            entity.CreatedAt);

        conversation.ChangeStatus(
            FromDataverseStatus(entity.Status));

        if (entity.LastMessageOn.HasValue)
        {
            conversation.RecordMessage(
                entity.LastMessageOn.Value);
        }

        return conversation;
    }

    private static int ToDataverseStatus(
        ConversationStatus status)
    {
        return status switch
        {
            ConversationStatus.Active =>
                DataverseStatusActive,

            ConversationStatus.Review =>
                DataverseStatusReview,

            ConversationStatus.Finalized =>
                DataverseStatusFinalized,

            ConversationStatus.Archived =>
                DataverseStatusArchived,

            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported ConversationStatus.")
        };
    }

    private static ConversationStatus FromDataverseStatus(
        int value)
    {
        return value switch
        {
            // 0 is DataverseContext's sentinel for a missing/unset
            // du_conversationstatus attribute, not a real OptionSet code -
            // treat it as Active, the status every conversation is created with.
            0 =>
                ConversationStatus.Active,

            DataverseStatusActive =>
                ConversationStatus.Active,

            DataverseStatusReview =>
                ConversationStatus.Review,

            DataverseStatusFinalized =>
                ConversationStatus.Finalized,

            DataverseStatusArchived =>
                ConversationStatus.Archived,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse ConversationStatus value.")
        };
    }

    private static int ToDataverseType(
        ConversationType type)
    {
        return type switch
        {
            ConversationType.Standalone =>
                DataverseTypeStandalone,

            ConversationType.Main =>
                DataverseTypeMain,

            ConversationType.SubChat =>
                DataverseTypeSubChat,

            ConversationType.Workspace =>
                DataverseTypeWorkspace,

            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported ConversationType.")
        };
    }

    private static ConversationType FromDataverseType(
        int value)
    {
        return value switch
        {
            DataverseTypeStandalone =>
                ConversationType.Standalone,

            DataverseTypeMain =>
                ConversationType.Main,

            DataverseTypeSubChat =>
                ConversationType.SubChat,

            DataverseTypeWorkspace =>
                ConversationType.Workspace,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse ConversationType value.")
        };
    }

    private static int ToDataverseVisibility(
        ConversationVisibility visibility)
    {
        return visibility switch
        {
            ConversationVisibility.Private =>
                DataverseVisibilityPrivate,

            ConversationVisibility.Workspace =>
                DataverseVisibilityWorkspace,

            ConversationVisibility.Project =>
                DataverseVisibilityProject,

            ConversationVisibility.Shared =>
                DataverseVisibilityShared,

            _ => throw new ArgumentOutOfRangeException(
                nameof(visibility),
                visibility,
                "Unsupported ConversationVisibility.")
        };
    }

    private static ConversationVisibility FromDataverseVisibility(
        int value)
    {
        return value switch
        {
            DataverseVisibilityPrivate =>
                ConversationVisibility.Private,

            DataverseVisibilityWorkspace =>
                ConversationVisibility.Workspace,

            DataverseVisibilityProject =>
                ConversationVisibility.Project,

            DataverseVisibilityShared =>
                ConversationVisibility.Shared,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse ConversationVisibility value.")
        };
    }
}