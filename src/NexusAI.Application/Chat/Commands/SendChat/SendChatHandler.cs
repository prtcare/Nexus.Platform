using NexusAI.Application.Chat.Prompting;
using NexusAI.Application.Knowledge.Services;
using NexusAI.Application.Providers;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.ConversationMessage;
using NexusAI.Domain.Project;

namespace NexusAI.Application.Chat.Commands.SendChat;

public sealed class SendChatHandler
{
    private readonly ILLMProvider _provider;
    private readonly IConversationRepository _conversationRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IKnowledgeRetrievalService _knowledgeRetrievalService;
    private readonly IProjectRepository _projectRepository;
    private readonly IPromptBuilder _promptBuilder;

    public SendChatHandler(
        ILLMProvider provider,
        IConversationRepository conversationRepository,
        IConversationMessageRepository messageRepository,
        IKnowledgeRetrievalService knowledgeRetrievalService,
        IProjectRepository projectRepository,
        IPromptBuilder promptBuilder)
    {
        _provider = provider;
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _knowledgeRetrievalService = knowledgeRetrievalService;
        _projectRepository = projectRepository;
        _promptBuilder = promptBuilder;
    }

    public async Task<SendChatResult> HandleAsync(
        SendChatCommand command,
        CancellationToken cancellationToken = default)
    {
        var conversation =
            await _conversationRepository.GetAsync(
                command.ConversationId,
                cancellationToken);

        if (conversation is null)
        {
            return new SendChatResult(
                false,
                string.Empty,
                "Conversation not found.");
        }

        var project =
            await _projectRepository.GetAsync(
                conversation.ProjectId,
                cancellationToken);

        if (project is null)
        {
            return new SendChatResult(
                false,
                string.Empty,
                "Project not found.");
        }

        var userMessage = new ConversationMessage(
            ConversationMessageId.New(),
            conversation.Id,
            ConversationMessageRole.User,
            command.Prompt);

        await _messageRepository.AddAsync(
            userMessage,
            cancellationToken);

        conversation.RecordMessage(
            userMessage.CreatedOn);

        await _conversationRepository.UpdateAsync(
            conversation,
            cancellationToken);

        var history =
            await _messageRepository.ListByConversationAsync(
                conversation.Id,
                cancellationToken);


        var knowledge =
            await _knowledgeRetrievalService.RetrieveAsync(
                project.WorkspaceId.Value,
                command.Prompt,
                cancellationToken);

        var chatRequest =
            _promptBuilder.Build(
                new PromptContext
                {
                    UserPrompt = command.Prompt,
                    History = history,
                    Knowledge = knowledge
                });

        var response =
            await _provider.ChatAsync(
                chatRequest,
                cancellationToken);

        if (!response.Success)
        {
            return new SendChatResult(
                false,
                string.Empty,
                response.Error);
        }

        var assistantMessage = new ConversationMessage(
            ConversationMessageId.New(),
            conversation.Id,
            ConversationMessageRole.Assistant,
            response.Text);

        await _messageRepository.AddAsync(
            assistantMessage,
            cancellationToken);

        conversation.RecordMessage(
            assistantMessage.CreatedOn);

        await _conversationRepository.UpdateAsync(
            conversation,
            cancellationToken);

        return new SendChatResult(
            true,
            response.Text,
            null);
    }
}