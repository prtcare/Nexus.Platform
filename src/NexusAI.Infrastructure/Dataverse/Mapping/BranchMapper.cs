using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class BranchMapper
    : IRepositoryMapper<Branch, BranchEntity>
{
    private const int DataverseActive = 121930000;
    private const int DataverseMerged = 121930001;
    private const int DataverseArchived = 121930002;

    public BranchEntity ToEntity(Branch domain)
    {
        return new BranchEntity
        {
            Id = domain.Id.Value,
            ConversationId = domain.ConversationId.Value,
            Name = domain.Name,
            Description = domain.Description,
            Status = ToDataverseStatus(domain.Status),
            CreatedAt = domain.CreatedAt
        };
    }

    public Branch ToDomain(BranchEntity entity)
    {
        return new Branch(
            new BranchId(entity.Id),
            new ConversationId(entity.ConversationId),
            entity.Name,
            entity.Description,
            FromDataverseStatus(entity.Status),
            entity.CreatedAt);
    }

    private static int ToDataverseStatus(
        BranchStatus status)
    {
        return status switch
        {
            BranchStatus.Active =>
                DataverseActive,

            BranchStatus.Merged =>
                DataverseMerged,

            BranchStatus.Archived =>
                DataverseArchived,

            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported BranchStatus value.")
        };
    }

    private static BranchStatus FromDataverseStatus(
        int value)
    {
        return value switch
        {
            // 0 is DataverseContext's sentinel for a missing/unset
            // du_branchstatus attribute, not a real OptionSet code
            // (real codes start at 121930000) - treat it as Active,
            // the status every branch is created with.
            0 =>
                BranchStatus.Active,

            DataverseActive =>
                BranchStatus.Active,

            DataverseMerged =>
                BranchStatus.Merged,

            DataverseArchived =>
                BranchStatus.Archived,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse BranchStatus value.")
        };
    }
}