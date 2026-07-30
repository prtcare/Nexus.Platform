using Microsoft.Extensions.DependencyInjection;

namespace NexusAI.Core.Modules;

public interface INexusModule
{
    void Register(IServiceCollection services);
}