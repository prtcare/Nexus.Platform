using Microsoft.Extensions.DependencyInjection;
using NexusAI.Core.Abstractions;
using NexusAI.Core.Agents;
using NexusAI.Core.Modules;
using NexusAI.Infrastructure.Agents;
using NexusAI.Infrastructure.Services;
namespace NexusAI.Infrastructure.Modules;


public sealed class CoreModule : INexusModule
{
    public void Register(IServiceCollection services)
    {
    
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAgentRuntime, AgentRuntime>();


    }
}