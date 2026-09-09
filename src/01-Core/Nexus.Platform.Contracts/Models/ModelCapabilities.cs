namespace Nexus.Platform.Contracts.Models;

[Flags]
public enum ModelCapabilities
{
    None = 0,
    Chat = 1,
    Reasoning = 2,
    ToolUse = 4,
    Vision = 8,
    Streaming = 16,
    StructuredOutput = 32,
    LongContext = 64
}
