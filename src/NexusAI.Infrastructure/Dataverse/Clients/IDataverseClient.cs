namespace NexusAI.Infrastructure.Dataverse.Clients;

public interface IDataverseClient
{
    IDataverseContext Context { get; }
}