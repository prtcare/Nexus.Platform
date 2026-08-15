namespace NexusAI.Application.Providers;

public sealed class ChatResponse
{
    public required string Text { get; init; }

    public bool Success { get; init; }

    public string? Error { get; init; }
}