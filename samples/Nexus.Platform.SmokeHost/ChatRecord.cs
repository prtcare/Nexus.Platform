using Nexus.Platform.Contracts.Models;

namespace Nexus.Platform.SmokeHost;

/// <summary>
/// A persisted chat turn. In a real product the conversation lives in the product's
/// store (Dataverse); the platform holds no product data by design. For the restart
/// smoke test the host keeps a durable local record so a fresh process can prove the
/// assistant message survived a real process restart.
/// </summary>
public sealed record ChatRecord(
    string Id,
    string Prompt,
    string? AssistantContent,
    string ModelUsed,
    int TokensIn,
    int TokensOut,
    DateTimeOffset RecordedAt)
{
    public static ChatRecord From(ModelInvocationResult result, string prompt) => new(
        Guid.NewGuid().ToString("N"),
        prompt,
        result.Message?.Content,
        result.ModelUsed ?? string.Empty,
        result.Usage.TokensIn,
        result.Usage.TokensOut,
        DateTimeOffset.UtcNow);
}
