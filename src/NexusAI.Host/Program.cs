using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexusAI.Agents.Developer;
using NexusAI.Host.Extensions;
using NexusAI.Core.Agents;
using NexusAI.Application.DependencyInjection;
using NexusAI.Infrastructure.DependencyInjection;
using NexusAI.Application.Workspaces.Commands.CreateWorkspace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddNexusAI();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddSingleton<DeveloperAgent>();

var app = builder.Build();
using var scope = app.Services.CreateScope();

var handler = scope.ServiceProvider
    .GetRequiredService<CreateWorkspaceHandler>();

var result = await handler.HandleAsync(
    new CreateWorkspaceCommand("Default Workspace"),
    CancellationToken.None);

Console.WriteLine();
Console.WriteLine("Workspace Created");
Console.WriteLine($"Id   : {result.WorkspaceId}");
Console.WriteLine($"Name : {result.Name}");
Console.WriteLine();
var runtime = app.Services.GetRequiredService<IAgentRuntime>();
var agent = app.Services.GetRequiredService<DeveloperAgent>();

await runtime.RunAsync(
    agent,
    new AgentContext
    {
        ConversationId = Guid.NewGuid().ToString(),
        WorkspaceId = "Default"
    });


app.Run();