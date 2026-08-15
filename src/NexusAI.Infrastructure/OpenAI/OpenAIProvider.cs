using Microsoft.Extensions.Options;
using NexusAI.Application.Providers;
using NexusAI.Domain.ConversationMessage;
using global::OpenAI.Chat;
using OpenAIChatMessage = global::OpenAI.Chat.ChatMessage;
using ProviderChatMessage = NexusAI.Application.Providers.ChatMessage;


namespace NexusAI.Infrastructure.OpenAI;

public sealed class OpenAIProvider : ILLMProvider
{
    private readonly ChatClient _chatClient;

    public OpenAIProvider(
        IOptions<OpenAIOptions> options)
    {
        var settings = options.Value;

        _chatClient = new ChatClient(
            model: settings.Model,
            apiKey: settings.ApiKey);
    }

    public async Task<ChatResponse> ChatAsync(
    ChatRequest request,
    CancellationToken cancellationToken = default)
    {
        try
        {

            var messages = new List<OpenAIChatMessage>();

            foreach (var message in request.History)
            {
                messages.Add(message.Role switch
                {
                    ConversationMessageRole.System =>
                        global::OpenAI.Chat.ChatMessage.CreateSystemMessage(message.Content),

                    ConversationMessageRole.Assistant =>
                        global::OpenAI.Chat.ChatMessage.CreateAssistantMessage(message.Content),

                    _ =>
                        global::OpenAI.Chat.ChatMessage.CreateUserMessage(message.Content)
                });
            }
            messages.Add(
                OpenAIChatMessage.CreateUserMessage(request.Prompt));

            var completion =
                await _chatClient.CompleteChatAsync(
                    messages,
                    cancellationToken: cancellationToken);

            return new ChatResponse
            {
                Success = true,
                Text = completion.Value.Content[0].Text,
                Error = string.Empty
            };
        }
        catch (Exception ex)
        {
            return new ChatResponse
            {
                Success = false,
                Text = string.Empty,
                Error = ex.Message
            };
        }
    }
}