using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexusAI.Agents.Developer;
using NexusAI.Application.Adr.Commands;
using NexusAI.Application.Artifact.Commands;
using NexusAI.Application.Branch.Commands;
using NexusAI.Application.Chat;
using NexusAI.Application.Chat.Commands.SendChat;
using NexusAI.Application.Conversations.Commands.CreateConversation;
using NexusAI.Application.DependencyInjection;
using NexusAI.Application.Execution;
using NexusAI.Application.Execution.Commands;
using NexusAI.Application.Knowledge.Commands;
using NexusAI.Application.Planning;
using NexusAI.Application.Planning.Commands;
using NexusAI.Application.Projects.Commands.CreateProject;
using NexusAI.Application.Providers;
using NexusAI.Application.Snapshot.Commands;
using NexusAI.Application.WorkItem;
using NexusAI.Application.Workspaces.Commands.CreateWorkspace;
using NexusAI.Core.Agents;
using NexusAI.Domain.Adr;
using NexusAI.Domain.Artifact;
using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Knowledge;
using NexusAI.Domain.Project;
using NexusAI.Domain.Session;
using NexusAI.Domain.Snapshot;
using NexusAI.Domain.WorkItem;
using NexusAI.Domain.Workspace;
using NexusAI.Host.Extensions;
using NexusAI.Infrastructure.DependencyInjection;
using NexusAI.Domain.Common.Identifiers;
using NexusAI.Application.Session.Commands;



var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(
        AppContext.BaseDirectory,
        "appsettings.json"),
    optional: false,
    reloadOnChange: false);

builder.Services.AddNexusAI();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<DeveloperAgent>();

builder.Services.AddSingleton<IAgent>(
    serviceProvider =>
        serviceProvider.GetRequiredService<DeveloperAgent>());
var app = builder.Build();
using var scope = app.Services.CreateScope();

var agentRegistry =
    scope.ServiceProvider.GetRequiredService<IAgentRegistry>();

var registeredAgents = agentRegistry.GetAll();

Console.WriteLine();
Console.WriteLine("Agent Registry");
Console.WriteLine("--------------");

foreach (var registeredAgent in registeredAgents)
{
    Console.WriteLine(
        $"{registeredAgent.Metadata.Id} - " +
        $"{registeredAgent.Metadata.Name}");
}

var developerAgent =
    agentRegistry.GetAgent(AgentType.Developer);

Console.WriteLine();
Console.WriteLine(
    $"Developer Agent Resolved: " +
    $"{developerAgent.Metadata.Name}");

var handler = scope.ServiceProvider
    .GetRequiredService<CreateWorkspaceHandler>();

var result = await handler.HandleAsync(
    new CreateWorkspaceCommand(
        "Default Workspace",
        "Default Owner",
        "Default Workspace Description"),
    CancellationToken.None);

Console.WriteLine();
Console.WriteLine("Workspace Created");
Console.WriteLine($"Id          : {result.WorkspaceId}");
Console.WriteLine($"Name        : {result.Name}");



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
Console.WriteLine($"Found       : {workspace is not null}");
Console.WriteLine($"Name        : {workspace?.Name}");
Console.WriteLine($"Owner       : {workspace?.Owner}");
Console.WriteLine($"Description : {workspace?.Description}");
Console.WriteLine($"Status      : {workspace?.Status}");
Console.WriteLine();
Console.WriteLine();
var runtime =
    app.Services.GetRequiredService<IAgentRuntime>();

var agent =
    agentRegistry.GetAgent(AgentType.Developer);

var conversationHandler = scope.ServiceProvider
    .GetRequiredService<CreateConversationHandler>();

var conversationResult =
    await conversationHandler.HandleAsync(
       new CreateConversationCommand(
    projectResult.ProjectId,
    workspace!.Id,
    "Architecture Discussion",
    "Initial architecture discussion.",
    ConversationType.Standalone,
    ConversationVisibility.Project),
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
        projectResult.ProjectId,
        "Implementation Repository",
        "Implementation repository work item.",
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

var knowledgeHandler =
    scope.ServiceProvider.GetRequiredService<CreateKnowledgeHandler>();

var knowledgeResult =
    await knowledgeHandler.HandleAsync(
        new CreateKnowledgeCommand(
            workspace.Id,
            "Architecture ADR",
            "Established reusable generic Dataverse repository infrastructure.",
            KnowledgeType.Documentation));

Console.WriteLine();
Console.WriteLine("Knowledge Created");
Console.WriteLine($"Id : {knowledgeResult.KnowledgeId}");

var knowledgeRepository =
    scope.ServiceProvider.GetRequiredService<IKnowledgeRepository>();

var knowledge =
    await knowledgeRepository.GetAsync(knowledgeResult.KnowledgeId);

Console.WriteLine();
Console.WriteLine("Knowledge Repository Verification");
Console.WriteLine($"Found : {knowledge is not null}");
Console.WriteLine($"Title : {knowledge?.Title}");
Console.WriteLine($"Type : {knowledge?.Type}");
Console.WriteLine($"Workspace Id : {knowledge?.WorkspaceId}");

var branchHandler =
    scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();

var branchResult =
    await branchHandler.HandleAsync(
        new CreateBranchCommand(
            conversation.Id,
            "Main",
            "Primary branch for this conversation"));

Console.WriteLine();
Console.WriteLine("Branch Created");
Console.WriteLine($"Id : {branchResult.BranchId}");

var branchRepository =
    scope.ServiceProvider.GetRequiredService<IBranchRepository>();

var branch =
    await branchRepository.GetAsync(branchResult.BranchId);

Console.WriteLine();
Console.WriteLine("Branch Repository Verification");
Console.WriteLine($"Found : {branch is not null}");
Console.WriteLine($"Name : {branch?.Name}");
Console.WriteLine($"Status : {branch?.Status}");
Console.WriteLine($"Conversation Id : {branch?.ConversationId}");

var artifactHandler =
    scope.ServiceProvider.GetRequiredService<CreateArtifactHandler>();

var artifactResult =
    await artifactHandler.HandleAsync(
        new CreateArtifactCommand(
            workItemResult.WorkItemId,
            "ImplementationPlan.md",
            ArtifactType.Document,
            "# Repository Pattern"));

Console.WriteLine();
Console.WriteLine("Artifact Created");
Console.WriteLine($"Id : {artifactResult.ArtifactId}");

var artifactRepository =
    scope.ServiceProvider.GetRequiredService<IArtifactRepository>();

var artifact =
    await artifactRepository.GetAsync(artifactResult.ArtifactId);

Console.WriteLine();
Console.WriteLine("Artifact Repository Verification");
Console.WriteLine($"Found : {artifact is not null}");
Console.WriteLine($"Name : {artifact?.Name}");
Console.WriteLine($"Type : {artifact?.Type}");
Console.WriteLine($"WorkItem Id : {artifact?.WorkItemId}");

var adrHandler =
    scope.ServiceProvider.GetRequiredService<CreateAdrHandler>();

var adrResult =
    await adrHandler.HandleAsync(
        new CreateAdrCommand(
            knowledgeResult.KnowledgeId,
            "Repository Pattern",
            "Use a generic Dataverse repository base."));

Console.WriteLine();
Console.WriteLine("ADR Created");
Console.WriteLine($"Id : {adrResult.AdrId}");

var adrRepository =
    scope.ServiceProvider.GetRequiredService<IAdrRepository>();

var adr =
    await adrRepository.GetAsync(adrResult.AdrId);

Console.WriteLine();
Console.WriteLine("ADR Repository Verification");
Console.WriteLine($"Found : {adr is not null}");
Console.WriteLine($"Title : {adr?.Title}");
Console.WriteLine($"Status : {adr?.Status}");
Console.WriteLine($"Knowledge Id : {adr?.KnowledgeId}");


var snapshotHandler =
    scope.ServiceProvider.GetRequiredService<CreateSnapshotHandler>();

var snapshotResult =
    await snapshotHandler.HandleAsync(
        new CreateSnapshotCommand(
            branchResult.BranchId,
            conversation.Id,
            "Initial Snapshot",
            "Initial conversation and branch state"));

Console.WriteLine();
Console.WriteLine("Snapshot Created");
Console.WriteLine($"Id : {snapshotResult.SnapshotId}");

var snapshotRepository =
    scope.ServiceProvider.GetRequiredService<ISnapshotRepository>();

var snapshot =
    await snapshotRepository.GetAsync(snapshotResult.SnapshotId);

Console.WriteLine();
Console.WriteLine("Snapshot Repository Verification");
Console.WriteLine($"Found           : {snapshot is not null}");

if (snapshot is not null)
{
    Console.WriteLine($"Snapshot Id     : {snapshot.Id.Value}");
    Console.WriteLine($"Branch Id       : {snapshot.BranchId.Value}");
    Console.WriteLine($"Conversation Id : {snapshot.ConversationId.Value}");
    Console.WriteLine($"Name            : {snapshot.Name}");
    Console.WriteLine($"State           : {snapshot.State}");
    Console.WriteLine($"Created At      : {snapshot.CreatedAt:O}");
}


var planHandler =
    scope.ServiceProvider.GetRequiredService<CreatePlanHandler>();

var planResult =
    await planHandler.HandleAsync(
        new CreatePlanCommand(
            projectResult.ProjectId,
            "Build an AI-powered REST API"));

Console.WriteLine();
Console.WriteLine("Planner");
Console.WriteLine("-------");

foreach (var item in planResult.WorkItems)
{
    Console.WriteLine($"{item.Type} - {item.Title}");
}


var executePlanHandler =
    scope.ServiceProvider.GetRequiredService<ExecutePlanHandler>();

var execution =
    await executePlanHandler.HandleAsync(
        new ExecutePlanCommand(
            projectResult.ProjectId));

Console.WriteLine();
Console.WriteLine("Execution Result");
Console.WriteLine("----------------");
Console.WriteLine($"Success : {execution.Success}");
Console.WriteLine($"Executed Work Items : {execution.ExecutedWorkItems}");

var engine =
    scope.ServiceProvider.GetRequiredService<IExecutionEngine>();





var chatService =
    scope.ServiceProvider.GetRequiredService<IChatService>();

Console.WriteLine();
Console.WriteLine("Conversation Memory Test");
Console.WriteLine("------------------------");

var first =
    await chatService.SendAsync(
        conversation.Id,
        "My name is David.");

Console.WriteLine("User: My name is David.");
Console.WriteLine($"Assistant: {first.Response}");
Console.WriteLine();

var second =
    await chatService.SendAsync(
        conversation.Id,
        "What is my name?");

Console.WriteLine("User: What is my name?");
Console.WriteLine($"Assistant: {second.Response}");

if (!second.Success)
{
    Console.WriteLine($"Error: {second.Error}");
}


await chatService.SendAsync(
    conversation.Id,
    "My name is David.",
    CancellationToken.None);

await chatService.SendAsync(
    conversation.Id,
    "What is my name?",
    CancellationToken.None);



await runtime.RunAsync(
    agent,
    new AgentContext(
        projectResult.ProjectId.ToString(),
        conversation.Id.ToString(),
        workspace.Id.ToString(),
        AgentType.Developer),
    CancellationToken.None);



app.Run();