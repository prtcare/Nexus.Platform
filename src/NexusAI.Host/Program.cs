using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexusAI.Agents.Developer;
using NexusAI.Application.Conversations.Commands.CreateConversation;
using NexusAI.Application.DependencyInjection;
using NexusAI.Application.Projects.Commands.CreateProject;
using NexusAI.Application.Session.Commands;
using NexusAI.Application.WorkItem;
using NexusAI.Application.Workspaces.Commands.CreateWorkspace;
using NexusAI.Core.Agents;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Project;
using NexusAI.Domain.Session;
using NexusAI.Domain.WorkItem;
using NexusAI.Domain.Workspace;
using NexusAI.Host.Extensions;
using NexusAI.Infrastructure.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddNexusAI();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
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

var projectHandler = scope.ServiceProvider
    .GetRequiredService<CreateProjectHandler>();

var projectResult = await projectHandler.HandleAsync(
    new CreateProjectCommand(
        result.WorkspaceId,
        "NexusAI"));

Console.WriteLine();
Console.WriteLine("Project Created");
Console.WriteLine($"Id   : {projectResult.ProjectId}");
Console.WriteLine($"Name : {projectResult.Name}");



var repository = scope.ServiceProvider
    .GetRequiredService<IWorkspaceRepository>();

var workspace = await repository.GetAsync(
    result.WorkspaceId);

Console.WriteLine();
Console.WriteLine("Repository Verification");
Console.WriteLine($"Found : {workspace is not null}");
Console.WriteLine($"Name  : {workspace?.Name}");
Console.WriteLine();
Console.WriteLine();
var runtime = app.Services.GetRequiredService<IAgentRuntime>();
var agent = app.Services.GetRequiredService<DeveloperAgent>();

var conversationHandler = scope.ServiceProvider
    .GetRequiredService<CreateConversationHandler>();

var conversationResult =
    await conversationHandler.HandleAsync(
        new CreateConversationCommand(
            projectResult.ProjectId,
            "Architecture Discussion"),
        CancellationToken.None);

Console.WriteLine();
Console.WriteLine("Conversation Created");
Console.WriteLine($"Id   : {conversationResult.ConversationId}");
Console.WriteLine($"Title: {conversationResult.Title}");

var conversationRepository = scope.ServiceProvider
    .GetRequiredService<IConversationRepository>();

var conversation = await conversationRepository.GetAsync(
    conversationResult.ConversationId);

Console.WriteLine();
Console.WriteLine("Conversation Repository Verification");
Console.WriteLine($"Found      : {conversation is not null}");
Console.WriteLine($"Title      : {conversation?.Title}");
Console.WriteLine($"Project Id : {conversation?.ProjectId}");

var projectRepository = scope.ServiceProvider
    .GetRequiredService<IProjectRepository>();

var project = await projectRepository.GetAsync(projectResult.ProjectId);

Console.WriteLine();
Console.WriteLine("Project Repository Verification");
Console.WriteLine($"Found : {project is not null}");
Console.WriteLine($"Name  : {project?.Name}");
Console.WriteLine($"Workspace Id : {project?.WorkspaceId}");


var workItemHandler = scope.ServiceProvider.GetRequiredService<CreateWorkItemHandler>();

var workItemResult = await workItemHandler.HandleAsync(
    new CreateWorkItemCommand(
        project.Id,
        "Implement Repository Pattern",
        WorkItemType.Task));

Console.WriteLine();
Console.WriteLine("WorkItem Created");
Console.WriteLine($"Id    : {workItemResult.WorkItemId}");

var sessionHandler =
    scope.ServiceProvider.GetRequiredService<CreateSessionHandler>();

var sessionResult =
    await sessionHandler.HandleAsync(
        new CreateSessionCommand(conversation!.Id));

Console.WriteLine();
Console.WriteLine("Session Created");
Console.WriteLine($"Id : {sessionResult.SessionId}");

var sessionRepository =
    scope.ServiceProvider.GetRequiredService<ISessionRepository>();

var session =
    await sessionRepository.GetAsync(sessionResult.SessionId);

Console.WriteLine();
Console.WriteLine("Session Repository Verification");
Console.WriteLine($"Found : {session is not null}");
Console.WriteLine($"Status : {session?.Status}");
Console.WriteLine($"Conversation Id : {session?.ConversationId}");







await runtime.RunAsync(
    agent,
    new AgentContext
    {
        ConversationId = Guid.NewGuid().ToString(),
        WorkspaceId = "Default"
    });


app.Run();