namespace NexusAI.Api.Endpoints.Chat;

public sealed record SendChatResponse(
    bool Success,
    string Reply,
    string Error);