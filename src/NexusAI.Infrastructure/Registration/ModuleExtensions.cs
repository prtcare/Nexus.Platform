using Microsoft.Extensions.DependencyInjection;
using NexusAI.Domain.ConversationMessage;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;
using NexusAI.Infrastructure.Dataverse.Repositories;
using NexusAI.Infrastructure.Dataverse.Mapping;

namespace NexusAI.Infrastructure.Modules;

public static class ModuleExtensions
{
    public static IServiceCollection AddInfrastructureModules(this IServiceCollection services)
    {
        new CoreModule().Register(services);

        services.AddScoped<
    IRepositoryMapper<ConversationMessage, ConversationMessageEntity>,
    ConversationMessageMapper>();

        services.AddScoped<
    IConversationMessageRepository,
    ConversationMessageDataverseRepository>();

        return services;
    }
}