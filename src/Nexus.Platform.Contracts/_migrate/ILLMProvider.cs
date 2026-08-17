namespace NexusAI.Application.Providers;

public interface ILLMProvider
{
    Task<ChatResponse> ChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);
}