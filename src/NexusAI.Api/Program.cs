using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using NexusAI.Api.Endpoints.Branches;
using NexusAI.Api.Endpoints.Chat;
using NexusAI.Api.Endpoints.Conversations;
using NexusAI.Api.Endpoints.Knowledge;
using NexusAI.Api.Endpoints.Projects;
using NexusAI.Api.Endpoints.Sessions;
using NexusAI.Api.Endpoints.Snapshots;
using NexusAI.Api.Endpoints.WorkItems;
using NexusAI.Api.Endpoints.Workspaces;
using NexusAI.Infrastructure.DependencyInjection;


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);

// Add services to the container.
builder.Services.AddControllers();

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "NexusAI API",
            Version = "v1",
            Description = "REST API for NexusAI"
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "NexusAI API v1");
    options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapChatEndpoints();
app.MapConversationEndpoints();
app.MapProjectEndpoints();
app.MapWorkItemEndpoints();
app.MapKnowledgeEndpoints();
app.MapWorkspaceEndpoints();
app.MapConversationMessageEndpoints();
app.MapSnapshotEndpoints();
app.MapBranchEndpoints();
app.MapSessionEndpoints();



app.Run();