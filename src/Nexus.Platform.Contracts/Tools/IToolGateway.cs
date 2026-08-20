namespace Nexus.Platform.Contracts.Tools;

public interface IToolGateway
{
    Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken ct = default);
}
