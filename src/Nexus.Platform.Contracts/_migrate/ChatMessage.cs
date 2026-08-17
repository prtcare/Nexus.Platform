namespace NexusAI.Application.Providers;

public sealed class ChatMessage
{
    public required string Role { get; init; }

    public required string Content { get; init; }
}