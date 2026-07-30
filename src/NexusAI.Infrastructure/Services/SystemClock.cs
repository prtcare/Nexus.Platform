using NexusAI.Core.Abstractions;

namespace NexusAI.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}