using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Platform.Core.Models;

namespace Nexus.Platform.Providers.OpenAI;

public static class OpenAIServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAIModelProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.SectionName));

        services.AddSingleton<INamedModelGateway, OpenAIModelGateway>();
        services.AddSingleton<IModelCatalogSource, OpenAIModelCatalogSource>();

        return services;
    }
}
