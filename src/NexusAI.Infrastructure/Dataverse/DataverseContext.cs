using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NexusAI.Infrastructure.Dataverse.Entities;
namespace NexusAI.Infrastructure.Dataverse;

public sealed class DataverseContext : IDataverseContext, IDisposable
{
    private readonly ServiceClient _client;

    public DataverseContext(ServiceClient client)
    {
        _client = client;
    }

    public async Task CreateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var dataverseEntity = ToDataverseEntity(entity);

        await _client.CreateAsync(
            dataverseEntity,
            cancellationToken);
    }

    public async Task<T?> RetrieveAsync<T>(
        Guid id,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var logicalName = GetLogicalName<T>();

        var columns = new ColumnSet(true);

        var entity = await _client.RetrieveAsync(
            logicalName,
            id,
            columns,
            cancellationToken);

        if (entity is null)
        {
            return default;
        }

        return FromDataverseEntity<T>(entity);
    }

    public async Task UpdateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var dataverseEntity = ToDataverseEntity(entity);

        await _client.UpdateAsync(
            dataverseEntity,
            cancellationToken);
    }

    public async Task<IReadOnlyList<TEntity>> RetrieveMultipleAsync<TEntity>(
        Func<TEntity, bool> predicate,
        CancellationToken cancellationToken = default)
        where TEntity : DataverseEntity
    {
        var logicalName = GetLogicalName<TEntity>();

        var query = new QueryExpression(logicalName)
        {
            ColumnSet = new ColumnSet(true)
        };

        var result = await _client.RetrieveMultipleAsync(
            query,
            cancellationToken);

        return result.Entities
            .Select(FromDataverseEntity<TEntity>)
            .Where(predicate)
            .ToList();
    }

    private Entity ToDataverseEntity<T>(T source)
        where T : class
    {
        return source switch
        {
            WorkspaceEntity workspace => MapWorkspace(workspace),

            ProjectEntity project => MapProject(project),

            WorkItemEntity workItem => MapWorkItem(workItem),

            ConversationEntity conversation => MapConversation(conversation),

            ConversationMessageEntity message => MapConversationMessage(message),

            KnowledgeEntity knowledge =>
        MapKnowledge(knowledge),

            SnapshotEntity snapshot =>
                MapSnapshot(snapshot),

            _ => throw new NotSupportedException(
                $"Dataverse mapping is not supported for {typeof(T).Name}.")
        };
    }

    private static Entity MapWorkspace(WorkspaceEntity source)
    {
        return new Entity(
            "du_t_001_workspace",
            source.Id)
        {
            ["du_workspacename"] = source.Name,
            ["du_workspaceowner"] = source.Owner,
            ["du_description"] = source.Description,
            ["du_workspacestatus"] = new OptionSetValue(source.Status)
        };
    }

    private static Entity MapProject(ProjectEntity source)
    {
        const int dataverseActive = 121930001;
        const int dataverseArchived = 121930004;

        var dataverseStatus = source.Status switch
        {
            1 => dataverseActive,
            2 => dataverseArchived,

            _ => throw new ArgumentOutOfRangeException(
                nameof(source.Status),
                source.Status,
                "Unsupported ProjectStatus value.")
        };

        return new Entity(
            "du_t_005_project",
            source.Id)
        {
            ["du_projectname"] = source.Name,

            ["du_projectstatus"] =
                new OptionSetValue(dataverseStatus),

            ["du_workspace"] =
                new EntityReference(
                    "du_t_001_workspace",
                    source.WorkspaceId)
        };
    }

    private static Entity MapWorkItem(WorkItemEntity source)
    {
        return new Entity(
            "du_t_019_workitem",
            source.Id)
        {
            ["du_workitemname"] = source.Title,
            ["du_description"] = source.Description,
            ["du_workitemtype"] = new OptionSetValue(source.Type),
            ["du_workitemstatus"] = new OptionSetValue(source.Status),
            ["du_project"] =
                new EntityReference(
                    "du_t_005_project",
                    source.ProjectId)
        };
    }

    private static Entity MapConversation(ConversationEntity source)
    {
        var entity = new Entity(
            "du_t_010_conversation",
            source.Id)
        {
            ["du_conversationname"] = source.Title,
            ["du_description"] = source.Description,

            ["du_conversationtype01"] =
                new OptionSetValue(source.Type),

            ["du_conversationvisibility"] =
                new OptionSetValue(source.Visibility),

            ["du_conversationstatus"] =
                new OptionSetValue(source.Status),

            ["du_lastmessageon"] =
                source.LastMessageOn?.UtcDateTime,

            ["du_project"] =
                new EntityReference(
                    "du_t_005_project",
                    source.ProjectId),

            ["du_workspace"] =
                new EntityReference(
                    "du_t_001_workspace",
                    source.WorkspaceId)
        };

        return entity;
    }

    private static Entity MapConversationMessage(
    ConversationMessageEntity source)
    {
        return new Entity(
            "du_t_011_conversationmessage",
            source.Id)
        {
            ["du_conversation"] =
                new EntityReference(
                    "du_t_010_conversation",
                    source.ConversationId),

            ["du_agenttype"] =
                source.AgentType,

            ["du_content"] =
                source.Content
        };
    }

    private static Entity MapKnowledge(
    KnowledgeEntity source)
    {
        var entity = new Entity(
            "du_t_017_knowledge",
            source.Id)
        {
            ["du_workspace"] =
                new EntityReference(
                    "du_t_001_workspace",
                    source.WorkspaceId),

            ["du_title"] =
                source.Title,

            ["du_content"] =
                source.Content,

            ["du_knowledgetype"] =
                new OptionSetValue(source.Type),

            ["du_knowledgestatus"] =
                new OptionSetValue(source.Status)
        };

        if (source.ProjectId.HasValue &&
            source.ProjectId.Value != Guid.Empty)
        {
            entity["du_project"] =
                new EntityReference(
                    "du_t_005_project",
                    source.ProjectId.Value);
        }

        if (source.SourceConversationId.HasValue &&
            source.SourceConversationId.Value != Guid.Empty)
        {
            entity["du_sourceconversation"] =
                new EntityReference(
                    "du_t_010_conversation",
                    source.SourceConversationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(source.Summary))
        {
            entity["du_summary"] = source.Summary;
        }

        if (!string.IsNullOrWhiteSpace(source.Keywords))
        {
            entity["du_keywords"] = source.Keywords;
        }

        return entity;
    }

    private static Entity MapSnapshot(
    SnapshotEntity source)
    {
        if (source.BranchId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "SnapshotEntity.BranchId cannot be empty.");
        }

        if (source.ConversationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "SnapshotEntity.ConversationId cannot be empty.");
        }

        var entity = new Entity(
            "du_t_016_snapshot",
            source.Id)
        {
            ["du_branch"] =
                new EntityReference(
                    "du_t_015_branch",
                    source.BranchId),

            ["du_conversation"] =
                new EntityReference(
                    "du_t_010_conversation",
                    source.ConversationId),

            ["du_snapshotname"] =
                source.Name,

            ["du_state"] =
                source.State,

            ["du_createdon"] =
                source.CreatedAt.UtcDateTime
        };

        return entity;
    }

    private static T FromDataverseEntity<T>(Entity entity)
        where T : class
    {
        if (typeof(T) == typeof(WorkspaceEntity))
        {
            return (T)(object)new WorkspaceEntity
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(
                    "du_workspacename") ?? string.Empty,
                Owner = entity.GetAttributeValue<string>(
                    "du_workspaceowner") ?? string.Empty,
                Description = entity.GetAttributeValue<string>(
                    "du_description") ?? string.Empty,
                Status = entity.GetAttributeValue<OptionSetValue>(
                    "du_workspacestatus")?.Value ?? 0,
                CreatedAt = entity.GetAttributeValue<DateTime>(
                    "createdon")
            };
        }

        if (typeof(T) == typeof(ProjectEntity))
        {
            var workspace =
                entity.GetAttributeValue<EntityReference>(
                    "du_workspace");

            return (T)(object)new ProjectEntity
            {
                Id = entity.Id,
                WorkspaceId = workspace?.Id ?? Guid.Empty,
                Name = entity.GetAttributeValue<string>(
                    "du_projectname") ?? string.Empty,
                Status = entity.GetAttributeValue<OptionSetValue>(
                    "du_projectstatus")?.Value ?? 0,
                CreatedAt = entity.GetAttributeValue<DateTime>(
                    "createdon")
            };
        }

        if (typeof(T) == typeof(ConversationEntity))
        {
            var project =
                entity.GetAttributeValue<EntityReference>(
                    "du_project");

            var workspace =
                entity.GetAttributeValue<EntityReference>(
                    "du_workspace");

            return (T)(object)new ConversationEntity
            {
                Id = entity.Id,

                ProjectId = project?.Id ?? Guid.Empty,

                WorkspaceId = workspace?.Id ?? Guid.Empty,

                Title = entity.GetAttributeValue<string>(
                    "du_conversationname") ?? string.Empty,

                Description = entity.GetAttributeValue<string>(
                    "du_description"),

                Type = entity.GetAttributeValue<OptionSetValue>(
                    "du_conversationtype01")?.Value ?? 0,

                Visibility = entity.GetAttributeValue<OptionSetValue>(
                    "du_conversationvisibility")?.Value ?? 0,

                Status = entity.GetAttributeValue<OptionSetValue>(
                    "du_conversationstatus")?.Value ?? 0,

                LastMessageOn = entity.GetAttributeValue<DateTime?>(
                    "du_lastmessageon"),

                CreatedAt = entity.GetAttributeValue<DateTime>(
                    "createdon")
            };
        }



        if (typeof(T) == typeof(ConversationMessageEntity))
        {
            var conversation =
                entity.GetAttributeValue<EntityReference>(
                    "du_conversation");

            return (T)(object)new ConversationMessageEntity
            {
                Id = entity.Id,

                ConversationId =
                    conversation?.Id ?? Guid.Empty,

                AgentType =
                    entity.GetAttributeValue<string>(
                        "du_agenttype") ?? string.Empty,

                Content =
                    entity.GetAttributeValue<string>(
                        "du_content") ?? string.Empty,

                CreatedAt =
                    entity.GetAttributeValue<DateTime>(
                        "createdon")
            };
        }

        if (typeof(T) == typeof(KnowledgeEntity))
        {
            var workspace =
                entity.GetAttributeValue<EntityReference>(
                    "du_workspace");

            var project =
                entity.GetAttributeValue<EntityReference>(
                    "du_project");

            var sourceConversation =
                entity.GetAttributeValue<EntityReference>(
                    "du_sourceconversation");

            return (T)(object)new KnowledgeEntity
            {
                Id = entity.Id,

                WorkspaceId =
                    workspace?.Id ?? Guid.Empty,

                ProjectId =
                    project?.Id,

                SourceConversationId =
                    sourceConversation?.Id,

                Title =
                    entity.GetAttributeValue<string>(
                        "du_title") ?? string.Empty,

                Content =
                    entity.GetAttributeValue<string>(
                        "du_content") ?? string.Empty,

                Summary =
                    entity.GetAttributeValue<string>(
                        "du_summary"),

                Keywords =
                    entity.GetAttributeValue<string>(
                        "du_keywords"),

                Type =
                    entity.GetAttributeValue<OptionSetValue>(
                        "du_knowledgetype")?.Value ?? 0,

                Status =
                    entity.GetAttributeValue<OptionSetValue>(
                        "du_knowledgestatus")?.Value ?? 0,

                CreatedAt =
                    entity.GetAttributeValue<DateTime>(
                        "createdon")
            };
        }

        if (typeof(T) == typeof(SnapshotEntity))
        {
            var branch =
                entity.GetAttributeValue<EntityReference>(
                    "du_branch");

            var conversation =
                entity.GetAttributeValue<EntityReference>(
                    "du_conversation");

            var createdOn =
                entity.GetAttributeValue<DateTime?>(
                    "du_createdon");

            return (T)(object)new SnapshotEntity
            {
                Id = entity.Id,

                BranchId =
                    branch?.Id ?? Guid.Empty,

                ConversationId =
                    conversation?.Id ?? Guid.Empty,

                Name =
                    entity.GetAttributeValue<string>(
                        "du_snapshotname") ?? string.Empty,

                State =
                    entity.GetAttributeValue<string>(
                        "du_state") ?? string.Empty,

                CreatedAt =
                    createdOn.HasValue
                        ? new DateTimeOffset(
                            DateTime.SpecifyKind(
                                createdOn.Value,
                                DateTimeKind.Utc))
                        : new DateTimeOffset(
                            DateTime.SpecifyKind(
                                entity.GetAttributeValue<DateTime>(
                                    "createdon"),
                                DateTimeKind.Utc))
            };
        }


        throw new NotSupportedException(
            $"Dataverse mapping is not supported for {typeof(T).Name}.");
    }

    private static string GetLogicalName<T>()
        where T : class
    {
        if (typeof(T) == typeof(WorkspaceEntity))
            return "du_t_001_workspace";

        if (typeof(T) == typeof(ProjectEntity))
            return "du_t_005_project";

        if (typeof(T) == typeof(WorkItemEntity))
            return "du_t_019_workitem";

        if (typeof(T) == typeof(ConversationEntity))
            return "du_t_010_conversation";

        if (typeof(T) == typeof(ConversationMessageEntity))
            return "du_t_011_conversationmessage";

        if (typeof(T) == typeof(KnowledgeEntity))
            return "du_t_017_knowledge";

        if (typeof(T) == typeof(SnapshotEntity))
        {
            return "du_t_016_snapshot";
        }


        throw new NotSupportedException(
            $"Dataverse entity mapping is not supported for {typeof(T).Name}.");
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}