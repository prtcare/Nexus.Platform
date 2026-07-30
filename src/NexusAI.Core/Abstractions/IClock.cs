namespace NexusAI.Core.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}