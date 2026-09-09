using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Nexus.Platform.Contracts.Core;
using Nexus.Platform.Contracts.Models;
using Nexus.Platform.Core.Models;
using OpenAI.Chat;

namespace Nexus.Platform.Providers.OpenAI;

public sealed class OpenAIModelGateway : INamedModelGateway
{
    private readonly OpenAIOptions _options;
    private readonly IQuotaPolicy _quotaPolicy;
    private readonly IUsageMeter _usageMeter;
    private readonly IAuditLog _auditLog;

    public string Vendor => "openai";

    public OpenAIModelGateway(
        IOptions<OpenAIOptions> options,
        IQuotaPolicy quotaPolicy,
        IUsageMeter usageMeter,
        IAuditLog auditLog)
    {
        _options = options.Value;
        _quotaPolicy = quotaPolicy;
        _usageMeter = usageMeter;
        _auditLog = auditLog;
    }

    public async Task<ModelInvocationResult> InvokeAsync(ModelInvocation invocation, CancellationToken ct = default)
    {
        var verdict = await _quotaPolicy.CheckAsync(invocation.Identity, invocation.ModelId, ct);
        if (!verdict.Allowed)
        {
            await _auditLog.AppendAsync(AuditEntryFor(invocation, success: false, verdict.Reason), ct);

            return new ModelInvocationResult
            {
                Success = false,
                Error = verdict.Reason ?? "Quota exceeded",
                ModelUsed = invocation.ModelId
            };
        }

        try
        {
            var chatClient = new ChatClient(model: ModelName(invocation.ModelId), apiKey: _options.ApiKey);
            var messages = invocation.Messages.Select(ToOpenAIMessage).ToList();

            var completion = await chatClient.CompleteChatAsync(messages, cancellationToken: ct);

            var usage = new ModelUsage(
                completion.Value.Usage?.InputTokenCount ?? 0,
                completion.Value.Usage?.OutputTokenCount ?? 0,
                0m);

            await _usageMeter.RecordAsync(UsageRecordFor(invocation, usage), ct);
            await _auditLog.AppendAsync(AuditEntryFor(invocation, success: true, "Invoked"), ct);

            return new ModelInvocationResult
            {
                Success = true,
                Message = new ModelMessage
                {
                    Role = ModelRole.Assistant,
                    Content = completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : string.Empty
                },
                Usage = usage,
                ModelUsed = invocation.ModelId
            };
        }
        catch (Exception ex)
        {
            await _auditLog.AppendAsync(AuditEntryFor(invocation, success: false, ex.Message), ct);

            return new ModelInvocationResult
            {
                Success = false,
                Error = ex.Message,
                ModelUsed = invocation.ModelId
            };
        }
    }

    public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(
        ModelInvocation invocation,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var verdict = await _quotaPolicy.CheckAsync(invocation.Identity, invocation.ModelId, ct);
        if (!verdict.Allowed)
        {
            await _auditLog.AppendAsync(AuditEntryFor(invocation, success: false, verdict.Reason), ct);
            throw new InvalidOperationException(verdict.Reason ?? "Quota exceeded");
        }

        var chatClient = new ChatClient(model: ModelName(invocation.ModelId), apiKey: _options.ApiKey);
        var messages = invocation.Messages.Select(ToOpenAIMessage).ToList();

        await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, cancellationToken: ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                yield return new ModelStreamChunk(part.Text, false);
            }
        }

        await _usageMeter.RecordAsync(UsageRecordFor(invocation, ModelUsage.Zero), ct);
        await _auditLog.AppendAsync(AuditEntryFor(invocation, success: true, "Streamed"), ct);

        yield return new ModelStreamChunk(string.Empty, true);
    }

    private static string ModelName(string modelId)
    {
        var separator = modelId.IndexOf(':');
        return separator >= 0 ? modelId[(separator + 1)..] : modelId;
    }

    private static ChatMessage ToOpenAIMessage(ModelMessage message) => message.Role switch
    {
        ModelRole.System => ChatMessage.CreateSystemMessage(message.Content),
        ModelRole.Assistant => ChatMessage.CreateAssistantMessage(message.Content),
        _ => ChatMessage.CreateUserMessage(message.Content)
    };

    private static UsageRecord UsageRecordFor(ModelInvocation invocation, ModelUsage usage) => new()
    {
        Identity = invocation.Identity,
        ModelId = invocation.ModelId,
        Usage = usage,
        RecordedAt = DateTimeOffset.UtcNow
    };

    private static AuditEntry AuditEntryFor(ModelInvocation invocation, bool success, string? detail) => new()
    {
        Identity = invocation.Identity,
        Action = $"model.invoke:{invocation.ModelId}",
        Success = success,
        Detail = detail,
        OccurredAt = DateTimeOffset.UtcNow
    };
}
